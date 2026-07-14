using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace KartRider.P236.Connector;

internal sealed record ClientSelection(string RootDirectory, string ExecutablePath)
{
    internal static ClientSelection FromPath(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new InvalidOperationException("클라이언트 폴더 또는 KartRider.exe를 선택하세요.");
        }

        string path = Path.GetFullPath(input.Trim().Trim('"'));
        string executablePath;
        string rootDirectory;
        if (Directory.Exists(path))
        {
            rootDirectory = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            executablePath = Path.Combine(rootDirectory, "KartRider.exe");
        }
        else if (File.Exists(path))
        {
            if (!string.Equals(Path.GetFileName(path), "KartRider.exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("실행 파일 이름은 정확히 KartRider.exe여야 합니다.");
            }

            executablePath = path;
            rootDirectory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("KartRider.exe의 폴더를 확인할 수 없습니다.");
        }
        else
        {
            throw new FileNotFoundException("선택한 클라이언트 경로가 없습니다.", path);
        }

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("선택한 폴더에 KartRider.exe가 없습니다.", executablePath);
        }

        return new ClientSelection(
            Path.GetFullPath(rootDirectory),
            Path.GetFullPath(executablePath));
    }
}

internal sealed record LaunchResult(int ProcessId, string RootDirectory, string Username);

internal static class OriginalClientValidator
{
    internal const string Sha256Hex =
        "81C6E1CD14102D3937DB9933FCF83908049132D6F0ACA9F6CA153C1D9D23797A";

    internal static bool IsKnownOriginal(string executablePath)
    {
        try
        {
            using FileStream stream = new FileStream(
                executablePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            byte[] hash = SHA256.HashData(stream);
            return string.Equals(Convert.ToHexString(hash), Sha256Hex, StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static PreparedPinInfo ValidateConfigurableClient(ClientSelection selection)
    {
        if (!IsKnownOriginal(selection.ExecutablePath))
        {
            throw new InvalidDataException(
                "선택한 파일은 지원되는 2005-12-14 원본 packed KartRider.exe가 아닙니다.");
        }

        string pinPath = Path.Combine(selection.RootDirectory, "KartRider.pin");
        string configPath = Path.Combine(selection.RootDirectory, "KartRider.xml");
        string dataPath = Path.Combine(selection.RootDirectory, "Data");
        string profilePath = Path.Combine(selection.RootDirectory, "Profile");
        if (!File.Exists(pinPath))
        {
            throw new FileNotFoundException("준비된 인스턴스의 KartRider.pin이 없습니다.", pinPath);
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException("준비된 인스턴스의 KartRider.xml이 없습니다.", configPath);
        }

        if (!Directory.Exists(dataPath))
        {
            throw new DirectoryNotFoundException($"준비된 인스턴스의 Data 폴더가 없습니다: {dataPath}");
        }

        if (!Directory.Exists(profilePath))
        {
            throw new DirectoryNotFoundException($"준비된 인스턴스의 Profile 폴더가 없습니다: {profilePath}");
        }

        return PreparedPinValidator.InspectConfigurable(selection.RootDirectory);
    }

    internal static PreparedPinInfo ValidatePreparedClient(ClientSelection selection)
    {
        _ = ValidateConfigurableClient(selection);
        return PreparedPinValidator.Validate(selection.RootDirectory);
    }

    internal static bool IsKnownPrepared(string executablePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(executablePath);
            string? rootDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                return false;
            }

            _ = ValidatePreparedClient(new ClientSelection(rootDirectory, fullPath));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            XmlException or ArgumentException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }

    internal static bool IsKnownConfigurable(string executablePath)
    {
        try
        {
            string fullPath = Path.GetFullPath(executablePath);
            string? rootDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                return false;
            }

            _ = ValidateConfigurableClient(new ClientSelection(rootDirectory, fullPath));
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            XmlException or ArgumentException or NotSupportedException or OverflowException)
        {
            return false;
        }
    }
}

internal sealed class LauncherService
{
    private readonly SemaphoreSlim _launchGate = new SemaphoreSlim(1, 1);
    private readonly MutexReleaseService _mutexReleaseService = new MutexReleaseService();

    internal async Task<LaunchResult> LaunchAsync(
        ClientSelection selection,
        string username,
        string serverAddress,
        ushort loginPort,
        string storageRoot,
        IEnumerable<string> knownInstanceExecutables,
        IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        await _launchGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => LaunchCore(
                    selection,
                    username,
                    serverAddress,
                    loginPort,
                    storageRoot,
                    knownInstanceExecutables,
                    progress,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private LaunchResult LaunchCore(
        ClientSelection selection,
        string username,
        string serverAddress,
        ushort loginPort,
        string storageRoot,
        IEnumerable<string> knownInstanceExecutables,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        string userSid;
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query))
        {
            userSid = identity.User?.Value
                ?? throw new InvalidOperationException("현재 Windows 사용자 SID를 확인할 수 없습니다.");
        }

        using Mutex processWideGate = new Mutex(
            initiallyOwned: false,
            name: $@"Local\KartRider2005Launcher.LaunchGate.{userSid}");
        bool ownsProcessWideGate = false;
        try
        {
            try
            {
                ownsProcessWideGate = processWideGate.WaitOne(TimeSpan.FromSeconds(30));
            }
            catch (AbandonedMutexException)
            {
                ownsProcessWideGate = true;
            }

            if (!ownsProcessWideGate)
            {
                throw new TimeoutException(
                    "다른 KartRider 2005 접속기가 실행 작업 중입니다. 잠시 후 다시 시도하세요.");
            }

            return LaunchWithProcessWideGate(
                selection,
                username,
                serverAddress,
                loginPort,
                storageRoot,
                knownInstanceExecutables,
                progress,
                cancellationToken);
        }
        finally
        {
            if (ownsProcessWideGate)
            {
                processWideGate.ReleaseMutex();
            }
        }
    }

    private LaunchResult LaunchWithProcessWideGate(
        ClientSelection selection,
        string username,
        string serverAddress,
        ushort loginPort,
        string storageRoot,
        IEnumerable<string> knownInstanceExecutables,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        PreparedClientSettings.RecoverIfNeeded(selection.RootDirectory);
        PreparedPinInfo pinInfo = OriginalClientValidator.ValidateConfigurableClient(selection);
        string normalizedUsername = LegacyAccountProfile.NormalizeUsername(username);
        string normalizedServerAddress = PreparedClientSettings.NormalizeServerAddress(serverAddress).ToString();
        string normalizedStorageRoot = PreparedClientSettings.NormalizeStorageRoot(storageRoot);
        progress?.Report(
            $"2005 원본 packed 실행 파일/PIN 확인: {pinInfo.LoginEndpoint}, storage={pinInfo.StorageRoot}");
        progress?.Report(
            $"적용할 PIN/XML 설정: {normalizedServerAddress}:{loginPort}, storage={normalizedStorageRoot}");

        HashSet<string> allowedExecutables = BuildVerifiedAllowedSet(
            selection,
            knownInstanceExecutables);
        // Compare the requested root rather than the copied PIN's current root.
        // This lets a copied client3 repair its client1 root before launch.
        EnsureUniqueStorageRoot(selection, normalizedStorageRoot, allowedExecutables);

        MutexReleaseSummary summary = default;
        // A just-started packed client may create its single-instance mutex after
        // protector initialization. Poll for up to five seconds before treating
        // an eligible process as already released/no-mutex.
        const int attempts = 20;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            summary = _mutexReleaseService.ReleaseExactMutexes(
                allowedExecutables,
                selection.ExecutablePath,
                progress);

            if (summary.EligibleProcessCount == 0 || summary.ReleasedCount > 0)
            {
                break;
            }

            if (attempt < attempts)
            {
                Thread.Sleep(250);
            }
        }

        if (summary.EligibleProcessCount > 0 && summary.ReleasedCount == 0)
        {
            progress?.Report(
                "실행 중인 준비 클라이언트에 CR-KartRider mutex가 없습니다. " +
                "이미 해제된 상태로 보고 계속합니다.");
        }

        // Do not mutate the selected instance until the duplicate-process and
        // mutex safety checks have succeeded. A rejected launch must leave the
        // saved username and active registry path untouched.
        PreparedClientSettingsResult settings = PreparedClientSettings.Apply(
            selection.RootDirectory,
            normalizedServerAddress,
            loginPort,
            normalizedStorageRoot);
        pinInfo = OriginalClientValidator.ValidatePreparedClient(selection);
        progress?.Report(settings.Changed
            ? $"PIN/XML 설정 저장 완료: {pinInfo.LoginEndpoint}, storage={pinInfo.StorageRoot}"
            : $"PIN/XML 설정 변경 없음: {pinInfo.LoginEndpoint}, storage={pinInfo.StorageRoot}");

        EnsureUniqueStorageRoot(selection, pinInfo.StorageRoot, allowedExecutables);

        string launcherProfile = LegacyAccountProfile.Write(selection.RootDirectory, normalizedUsername);
        progress?.Report($"AccountDataProfile 원본 기록: {launcherProfile}");

        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\TCGame\kart", writable: true)
            ?? throw new InvalidOperationException("HKCU\\Software\\TCGame\\kart 키를 만들 수 없습니다."))
        {
            key.SetValue("gamepath", selection.RootDirectory, RegistryValueKind.String);
            key.Flush();
        }
        progress?.Report($"HKCU gamepath 설정: {selection.RootDirectory}");

        DirectoryInfo? clientParent = Directory.GetParent(selection.RootDirectory);
        string instanceName = Path.GetFileName(
            selection.RootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (clientParent != null &&
            string.Equals(clientParent.Name, "clients", StringComparison.OrdinalIgnoreCase) &&
            instanceName.Length > 0 &&
            instanceName.All(character => char.IsLetterOrDigit(character) || character is '_' or '-'))
        {
            using RegistryKey instanceKey = Registry.CurrentUser.CreateSubKey(
                $@"Software\TCGame\kart\instances\{instanceName}",
                writable: true)
                ?? throw new InvalidOperationException("인스턴스 레지스트리 키를 만들 수 없습니다.");
            instanceKey.SetValue("gamepath", selection.RootDirectory, RegistryValueKind.String);
            instanceKey.Flush();
        }

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = selection.ExecutablePath,
            Arguments = "-profile:launcher",
            WorkingDirectory = selection.RootDirectory,
            UseShellExecute = true
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("KartRider.exe 프로세스를 시작하지 못했습니다.");
        if (process.WaitForExit(1500))
        {
            throw new InvalidOperationException(
                $"KartRider.exe가 시작 직후 종료했습니다 (PID {process.Id}, exit={process.ExitCode}). " +
                "실행 중인 다른 인스턴스와 PIN/서버 설정을 확인하세요.");
        }
        progress?.Report($"실행 완료: PID {process.Id}, username={normalizedUsername}");
        return new LaunchResult(process.Id, selection.RootDirectory, normalizedUsername);
    }

    private static HashSet<string> BuildVerifiedAllowedSet(
        ClientSelection selection,
        IEnumerable<string> knownInstanceExecutables)
    {
        HashSet<string> candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            selection.ExecutablePath
        };

        foreach (string path in knownInstanceExecutables)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(Path.GetFullPath(path));
            }
        }

        DirectoryInfo? parent = Directory.GetParent(selection.RootDirectory);
        if (parent != null && string.Equals(parent.Name, "clients", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string directory in Directory.EnumerateDirectories(parent.FullName, "*", SearchOption.TopDirectoryOnly))
            {
                string executable = Path.Combine(directory, "KartRider.exe");
                if (File.Exists(executable))
                {
                    candidates.Add(Path.GetFullPath(executable));
                }
            }
        }

        string selectedExecutable = Path.GetFullPath(selection.ExecutablePath);
        candidates.RemoveWhere(path =>
        {
            if (!string.Equals(Path.GetFileName(path), "KartRider.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(Path.GetFullPath(path), selectedExecutable, StringComparison.OrdinalIgnoreCase)
                ? !OriginalClientValidator.IsKnownConfigurable(path)
                : !OriginalClientValidator.IsKnownPrepared(path);
        });

        if (!candidates.Contains(selection.ExecutablePath))
        {
            throw new InvalidDataException("선택한 실행 파일이 안전한 mutex 대상 집합에서 제외되었습니다.");
        }

        return candidates;
    }

    private static void EnsureUniqueStorageRoot(
        ClientSelection selection,
        string selectedStorageRoot,
        IEnumerable<string> allowedExecutables)
    {
        foreach (string executablePath in allowedExecutables)
        {
            if (string.Equals(executablePath, selection.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? otherRoot = Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(otherRoot))
            {
                continue;
            }

            try
            {
                PreparedPinInfo otherPin = PreparedPinValidator.Validate(otherRoot);
                if (string.Equals(
                        selectedStorageRoot,
                        otherPin.StorageRoot,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"두 준비 인스턴스가 같은 문서 저장 폴더를 사용합니다 " +
                        $"(storage={selectedStorageRoot}): {selection.RootDirectory}, {otherRoot}");
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
            {
                // Invalid/unprepared roots are never launchable, so they do not
                // participate in the prepared-instance storage uniqueness check.
            }
        }
    }
}

internal static class LegacyAccountProfile
{
    private const int MaximumUsernameLength = 32;

    internal static string NormalizeUsername(string value)
    {
        string username = (value ?? string.Empty).Trim();
        if (username.Length == 0)
        {
            throw new InvalidOperationException("username을 입력하세요.");
        }

        if (username.Length > MaximumUsernameLength)
        {
            throw new InvalidOperationException($"username은 {MaximumUsernameLength}자 이하여야 합니다.");
        }

        if (username.Any(char.IsControl))
        {
            throw new InvalidOperationException("username에는 줄바꿈, 탭 등의 제어 문자를 사용할 수 없습니다.");
        }

        try
        {
            XmlConvert.VerifyXmlChars(username);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("username에 XML에서 사용할 수 없는 문자가 있습니다.", exception);
        }

        return username;
    }

    internal static string Write(string rootDirectory, string username)
    {
        string path = Path.Combine(rootDirectory, "Profile", "launcher.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        XDocument document = File.Exists(path)
            ? LoadLegacyXml(path)
            : new XDocument(new XDeclaration("1.0", "utf-16", null), new XElement("profile"));

        if (document.Root == null ||
            !string.Equals(document.Root.Name.LocalName, "profile", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Profile\\launcher.xml의 루트가 profile이 아닙니다.");
        }

        XElement? usernameElement = document.Root.Elements().FirstOrDefault(element =>
            string.Equals(element.Name.LocalName, "username", StringComparison.OrdinalIgnoreCase));
        if (usernameElement == null)
        {
            usernameElement = new XElement("username");
            document.Root.Add(usernameElement);
        }

        usernameElement.Value = username;
        document.Declaration = new XDeclaration("1.0", "utf-16", null);
        SaveUtf16Atomically(path, document);
        return path;
    }

    internal static string? TryReadUsername(string rootDirectory)
    {
        try
        {
            string path = Path.Combine(rootDirectory, "Profile", "launcher.xml");
            if (!File.Exists(path))
            {
                return null;
            }

            XDocument document = LoadLegacyXml(path);
            return document.Root?.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "username", StringComparison.OrdinalIgnoreCase))?.Value.Trim();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
        {
            return null;
        }
    }

    private static XDocument LoadLegacyXml(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding;
        int preambleLength;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(false, true);
            preambleLength = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = Encoding.Unicode;
            preambleLength = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 2;
        }
        else if (LooksLikeUtf16(bytes, littleEndian: true))
        {
            encoding = Encoding.Unicode;
            preambleLength = 0;
        }
        else if (LooksLikeUtf16(bytes, littleEndian: false))
        {
            encoding = Encoding.BigEndianUnicode;
            preambleLength = 0;
        }
        else
        {
            encoding = new UTF8Encoding(false, true);
            preambleLength = 0;
        }

        string xml;
        try
        {
            xml = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }
        catch (DecoderFallbackException)
        {
            xml = Encoding.Latin1.GetString(bytes);
        }

        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using StringReader stringReader = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static bool LooksLikeUtf16(byte[] bytes, bool littleEndian)
    {
        int sampleLength = Math.Min(bytes.Length, 128);
        if (sampleLength < 4)
        {
            return false;
        }

        int expectedNulls = 0;
        int expectedSlots = 0;
        for (int index = littleEndian ? 1 : 0; index < sampleLength; index += 2)
        {
            expectedSlots++;
            if (bytes[index] == 0)
            {
                expectedNulls++;
            }
        }

        return expectedNulls >= Math.Max(2, expectedSlots * 3 / 4);
    }

    private static void SaveUtf16Atomically(string path, XDocument document)
    {
        string temporaryPath = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        string backupPath = path + ".KartRider2005Launcher.bak";
        try
        {
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = Encoding.Unicode,
                Indent = true,
                OmitXmlDeclaration = false,
                CloseOutput = false
            };
            using (FileStream stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    document.Save(writer);
                }
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Copy(path, backupPath, overwrite: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
