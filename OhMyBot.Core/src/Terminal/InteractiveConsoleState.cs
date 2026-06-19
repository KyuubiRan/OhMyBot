namespace OhMyBot.Core.Terminal;

public sealed class InteractiveConsoleState
{
    private const int MaxHistoryCount = 100;
    private const string Prompt = ">> ";

    private readonly Lock _gate = new();
    private readonly bool _enabled;
    private readonly List<string> _history = [];
    private InteractiveConsoleOutputQueue? _outputQueue;
    private string _currentInput = string.Empty;
    private int _cursorIndex;
    private int? _historyIndex;

    public InteractiveConsoleState()
    {
        _enabled = !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    public bool Enabled => _enabled;

    public void AttachOutputQueue(InteractiveConsoleOutputQueue outputQueue)
    {
        _outputQueue = outputQueue;
    }

    public void WriteLine(string message)
    {
        WriteLine(message, foregroundColor: null, backgroundColor: null);
    }

    public void WriteLine(string message, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor = null)
    {
        if (!_enabled || _outputQueue is null)
        {
            WriteColoredLine(message, foregroundColor, backgroundColor);
            return;
        }

        _outputQueue.TryEnqueue(new InteractiveConsoleOutputItem(
            [new ConsoleTextSegment(message, foregroundColor, backgroundColor)]));
    }

    public void WriteSegmentsLine(params ConsoleTextSegment[] segments)
    {
        if (!_enabled || _outputQueue is null)
        {
            WriteColoredSegmentsLine(segments);
            return;
        }

        _outputQueue.TryEnqueue(new InteractiveConsoleOutputItem(segments));
    }

    public void WriteSegmentsLineDirect(IReadOnlyList<ConsoleTextSegment> segments)
    {
        lock (_gate)
        {
            ClearPromptLine();
            WriteColoredSegmentsLine(segments);
            RenderPromptDirect();
        }
    }

    private static void WriteColoredLine(string message, ConsoleColor? foregroundColor, ConsoleColor? backgroundColor)
    {
        if (foregroundColor is null && backgroundColor is null)
        {
            Console.WriteLine(message);
            return;
        }

        var originalForegroundColor = Console.ForegroundColor;
        var originalBackgroundColor = Console.BackgroundColor;
        try
        {
            if (foregroundColor is not null)
            {
                Console.ForegroundColor = foregroundColor.Value;
            }

            if (backgroundColor is not null)
            {
                Console.BackgroundColor = backgroundColor.Value;
            }

            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = originalForegroundColor;
            Console.BackgroundColor = originalBackgroundColor;
        }
    }

    private static void WriteColoredSegmentsLine(IReadOnlyList<ConsoleTextSegment> segments)
    {
        var originalForegroundColor = Console.ForegroundColor;
        var originalBackgroundColor = Console.BackgroundColor;
        try
        {
            foreach (var segment in segments)
            {
                Console.ForegroundColor = segment.ForegroundColor ?? originalForegroundColor;
                Console.BackgroundColor = segment.BackgroundColor ?? originalBackgroundColor;
                Console.Write(segment.Text);
            }

            Console.WriteLine();
        }
        finally
        {
            Console.ForegroundColor = originalForegroundColor;
            Console.BackgroundColor = originalBackgroundColor;
        }
    }

    public void RenderPrompt()
    {
        if (!_enabled)
        {
            return;
        }

        lock (_gate)
        {
            RenderPromptDirect();
        }
    }

    public void Append(char character)
    {
        if (!_enabled)
        {
            return;
        }

        lock (_gate)
        {
            _currentInput = _currentInput.Insert(_cursorIndex, character.ToString());
            _cursorIndex++;
            _historyIndex = null;
            RenderPromptDirect();
        }
    }

    public void Backspace()
    {
        if (!_enabled || _currentInput.Length == 0 || _cursorIndex == 0)
        {
            return;
        }

        lock (_gate)
        {
            _currentInput = _currentInput.Remove(_cursorIndex - 1, 1);
            _cursorIndex--;
            _historyIndex = null;
            RenderPromptDirect();
        }
    }

    public void MoveCursorLeft()
    {
        if (!_enabled || _cursorIndex == 0)
        {
            return;
        }

        lock (_gate)
        {
            _cursorIndex--;
            RenderPromptDirect();
        }
    }

    public void MoveCursorRight()
    {
        if (!_enabled || _cursorIndex >= _currentInput.Length)
        {
            return;
        }

        lock (_gate)
        {
            _cursorIndex++;
            RenderPromptDirect();
        }
    }

    public string CommitInput()
    {
        if (!_enabled)
        {
            return string.Empty;
        }

        lock (_gate)
        {
            var input = _currentInput;
            AddHistory(input);
            _currentInput = string.Empty;
            _cursorIndex = 0;
            _historyIndex = null;
            Console.WriteLine();
            return input;
        }
    }

    public void PreviousHistory()
    {
        if (!_enabled || _history.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            _historyIndex = _historyIndex is null
                ? _history.Count - 1
                : Math.Max(0, _historyIndex.Value - 1);
            ReplaceCurrentInput(_history[_historyIndex.Value]);
        }
    }

    public void NextHistory()
    {
        if (!_enabled || _history.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_historyIndex is null)
            {
                return;
            }

            if (_historyIndex.Value >= _history.Count - 1)
            {
                _historyIndex = null;
                ReplaceCurrentInput(string.Empty);
                return;
            }

            _historyIndex++;
            ReplaceCurrentInput(_history[_historyIndex.Value]);
        }
    }

    private void AddHistory(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (_history.Count > 0 && string.Equals(_history[^1], input, StringComparison.Ordinal))
        {
            return;
        }

        _history.Add(input);
        if (_history.Count > MaxHistoryCount)
        {
            _history.RemoveAt(0);
        }
    }

    private void ReplaceCurrentInput(string input)
    {
        _currentInput = input;
        _cursorIndex = _currentInput.Length;
        ClearPromptLine();
        RenderPromptDirect();
    }

    private void RenderPromptDirect()
    {
        ClearPromptLine();
        Console.Write($"{Prompt}{_currentInput}");
        RestoreCursorPosition();
    }

    private void RestoreCursorPosition()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        var cursorLeft = Math.Min(Prompt.Length + _cursorIndex, Math.Max(0, Console.WindowWidth - 1));
        Console.SetCursorPosition(cursorLeft, Console.CursorTop);
    }

    private static void ClearPromptLine()
    {
        var width = Console.IsOutputRedirected ? 0 : Console.WindowWidth;
        if (width <= 0)
        {
            Console.WriteLine();
            return;
        }

        Console.Write('\r');
        Console.Write(new string(' ', width - 1));
        Console.Write('\r');
    }
}

public sealed record ConsoleTextSegment(
    string Text,
    ConsoleColor? ForegroundColor = null,
    ConsoleColor? BackgroundColor = null);
