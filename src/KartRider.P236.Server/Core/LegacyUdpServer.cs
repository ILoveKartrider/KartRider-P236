#nullable disable
using System;
using System.Net;
using System.Net.Sockets;
using KartRider.Common.Security;
using KartRider.IO;

namespace KartRider;

/// <summary>
/// Implements the UDP setup handshake required by the 2005 lobby.  The client
/// does not allow room creation until echo succeeds and three time-sync samples
/// have been accepted.
/// </summary>
internal sealed class LegacyUdpServer : IDisposable
{
	private const uint DatagramChecksumXor = 0x4F3816C3u;
	private const uint PqUdpEcho = 279905129u;
	private const uint PrUdpEcho = 280429418u;
	private const uint PqUdpTimeSync = 584516886u;
	private const uint PrUdpTimeSync = 585303319u;
	private readonly object _syncRoot = new object();
	private readonly IPAddress _bindAddress;
	private readonly int _port;
	private UdpClient _client;
	private bool _running;

	public LegacyUdpServer(IPAddress bindAddress, int port)
	{
		_bindAddress = bindAddress ?? throw new ArgumentNullException(nameof(bindAddress));
		_port = port;
	}

	public int LocalPort { get; private set; }

	public void Start()
	{
		lock (_syncRoot)
		{
			if (_running)
			{
				return;
			}

			UdpClient client = new UdpClient(new IPEndPoint(_bindAddress, _port));
			try
			{
				const int SioUdpConnectionReset = -1744830452;
				client.Client.IOControl(
					(IOControlCode)SioUdpConnectionReset,
					new byte[] { 0, 0, 0, 0 },
					null);
			}
			catch (SocketException)
			{
				// This optimization is Windows-specific and is not required to serve.
			}

			_client = client;
			LocalPort = ((IPEndPoint)client.Client.LocalEndPoint!).Port;
			_running = true;
			BeginReceive(client);
			LegacyPacketTrace.LogEvent($"[P236 UDP] Listening on {_bindAddress}:{LocalPort}.");
		}
	}

	public void Stop()
	{
		UdpClient client;
		lock (_syncRoot)
		{
			if (!_running)
			{
				return;
			}

			_running = false;
			client = _client;
			_client = null;
		}

		client?.Dispose();
		LegacyPacketTrace.LogEvent($"[P236 UDP] Stopped listener on port {LocalPort}.");
	}

	public void Dispose()
	{
		Stop();
	}

	private void BeginReceive(UdpClient client)
	{
		try
		{
			client.BeginReceive(EndReceive, client);
		}
		catch (ObjectDisposedException)
		{
		}
		catch (SocketException exception)
		{
			LegacyPacketTrace.LogEvent($"[2005 UDP] BeginReceive failed: {exception.Message}");
		}
	}

	private void EndReceive(IAsyncResult asyncResult)
	{
		UdpClient client = (UdpClient)asyncResult.AsyncState;
		try
		{
			IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
			byte[] datagram = client.EndReceive(asyncResult, ref remoteEndPoint);
			ProcessDatagram(client, datagram, remoteEndPoint);
		}
		catch (ObjectDisposedException)
		{
			return;
		}
		catch (SocketException exception) when (
			exception.SocketErrorCode == SocketError.ConnectionReset ||
			exception.SocketErrorCode == SocketError.OperationAborted ||
			exception.SocketErrorCode == SocketError.Interrupted)
		{
		}
		catch (Exception exception)
		{
			LegacyPacketTrace.LogEvent($"[2005 UDP] Receive failed: {exception.Message}");
		}
		finally
		{
			lock (_syncRoot)
			{
				if (_running && ReferenceEquals(_client, client))
				{
					BeginReceive(client);
				}
			}
		}
	}

	private static void ProcessDatagram(UdpClient client, byte[] datagram, IPEndPoint remoteEndPoint)
	{
		if (datagram == null || datagram.Length < 20)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 UDP] Ignored short datagram from {remoteEndPoint}: {datagram?.Length ?? 0} bytes.");
			return;
		}

		uint receiveTick = CurrentTick();
		uint iv = BitConverter.ToUInt32(datagram, 0);
		uint wireChecksum = BitConverter.ToUInt32(datagram, datagram.Length - sizeof(uint));
		byte[] payload = new byte[datagram.Length - (sizeof(uint) * 2)];
		Buffer.BlockCopy(datagram, sizeof(uint), payload, 0, payload.Length);

		uint payloadHash = KRPacketCrypto.HashDecrypt(payload, (uint)payload.Length, iv);
		uint expectedChecksum = iv ^ payloadHash ^ DatagramChecksumXor;
		if (wireChecksum != expectedChecksum)
		{
			LegacyPacketTrace.LogEvent(
				$"[2005 UDP] Checksum mismatch from {remoteEndPoint}: " +
				$"wire=0x{wireChecksum:X8}, expected=0x{expectedChecksum:X8}.");
			return;
		}

		using InPacket packet = new InPacket(payload);
		uint accountId = packet.ReadUInt();
		uint sessionToken = packet.ReadUInt();
		uint packetName = packet.ReadUInt();
		RouterListener.ObserveUdpEndPoint(accountId, remoteEndPoint);

		if (packetName == PqUdpEcho)
		{
			if (packet.Available != 4)
			{
				throw new InvalidOperationException(
					$"PqUdpEcho body length mismatch: expected 4, got {packet.Available}.");
			}

			uint echoToken = packet.ReadUInt();
			using OutPacket response = new OutPacket();
			response.WriteUInt(accountId);
			response.WriteUInt(sessionToken);
			response.WriteUInt(PrUdpEcho);
			response.WriteUInt(echoToken);
			Send(client, response, remoteEndPoint);
			LegacyPacketTrace.LogEvent(
				$"[2005 UDP] Echo account={accountId}, session=0x{sessionToken:X8}, " +
				$"token=0x{echoToken:X8}, endpoint={remoteEndPoint}.");
			return;
		}

		if (packetName == PqUdpTimeSync)
		{
			if (packet.Available != 4)
			{
				throw new InvalidOperationException(
					$"PqUdpTimeSync body length mismatch: expected 4, got {packet.Available}.");
			}

			uint clientSendTick = packet.ReadUInt();
			using OutPacket response = new OutPacket();
			response.WriteUInt(accountId);
			response.WriteUInt(sessionToken);
			response.WriteUInt(PrUdpTimeSync);
			response.WriteUInt(clientSendTick);
			response.WriteUInt(receiveTick);
			response.WriteUInt(CurrentTick());
			Send(client, response, remoteEndPoint);
			LegacyPacketTrace.LogEvent(
				$"[2005 UDP] Time sync account={accountId}, t1={clientSendTick}, " +
				$"t2={receiveTick}, endpoint={remoteEndPoint}.");
			return;
		}

		LegacyPacketTrace.LogEvent(
			$"[2005 UDP] Unhandled packet 0x{packetName:X8} from {remoteEndPoint}, " +
			$"body={packet.Available}.");
	}

	private static void Send(UdpClient client, OutPacket packet, IPEndPoint remoteEndPoint)
	{
		byte[] payload = packet.ToArray();
		uint iv = unchecked((uint)Random.Shared.Next());
		uint payloadHash = KRPacketCrypto.HashEncrypt(payload, (uint)payload.Length, iv);
		uint checksum = iv ^ payloadHash ^ DatagramChecksumXor;

		byte[] datagram = new byte[payload.Length + (sizeof(uint) * 2)];
		Buffer.BlockCopy(BitConverter.GetBytes(iv), 0, datagram, 0, sizeof(uint));
		Buffer.BlockCopy(payload, 0, datagram, sizeof(uint), payload.Length);
		Buffer.BlockCopy(
			BitConverter.GetBytes(checksum),
			0,
			datagram,
			datagram.Length - sizeof(uint),
			sizeof(uint));
		client.Send(datagram, datagram.Length, remoteEndPoint);
	}

	private static uint CurrentTick()
	{
		return unchecked((uint)Environment.TickCount64);
	}
}
