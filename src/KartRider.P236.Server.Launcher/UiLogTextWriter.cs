using System.Text;

namespace KartRider.P236.Server.Launcher;

internal sealed class UiLogTextWriter : TextWriter
{
    private readonly object _gate = new();
    private readonly StringBuilder _pending = new();
    private readonly Action<string> _writeLine;
    private readonly string _prefix;

    public UiLogTextWriter(Action<string> writeLine, string prefix = "")
    {
        _writeLine = writeLine ?? throw new ArgumentNullException(nameof(writeLine));
        _prefix = prefix;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        string? completeLine = null;
        lock (_gate)
        {
            if (value == '\n')
            {
                completeLine = TakeLineLocked();
            }
            else if (value != '\r')
            {
                _pending.Append(value);
            }
        }

        if (completeLine is not null)
        {
            _writeLine(_prefix + completeLine);
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }

        foreach (char character in value)
        {
            Write(character);
        }
    }

    public override void WriteLine(string? value)
    {
        if (value is not null)
        {
            Write(value);
        }

        Write('\n');
    }

    protected override void Dispose(bool disposing)
    {
        string? remaining = null;
        if (disposing)
        {
            lock (_gate)
            {
                if (_pending.Length > 0)
                {
                    remaining = TakeLineLocked();
                }
            }
        }

        if (remaining is not null)
        {
            _writeLine(_prefix + remaining);
        }

        base.Dispose(disposing);
    }

    private string TakeLineLocked()
    {
        string line = _pending.ToString();
        _pending.Clear();
        return line;
    }
}

internal sealed class TeeTextWriter : TextWriter
{
    private readonly TextWriter _first;
    private readonly TextWriter _second;

    public TeeTextWriter(TextWriter first, TextWriter second)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
    }

    public override Encoding Encoding => _first.Encoding;

    public override void Write(char value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void Write(string? value)
    {
        _first.Write(value);
        _second.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _first.WriteLine(value);
        _second.WriteLine(value);
    }

    public override void Flush()
    {
        _first.Flush();
        _second.Flush();
    }
}
