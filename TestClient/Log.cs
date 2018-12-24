using System;
using System.Threading;

public static class Logs
{
    [NotNull]
    public static Log Create(string name, string sourceName = null)
    {
        return new Log(name, sourceName);
    }

    private static LogLevel logLevel = LogLevel.Info;


    #region multithreading

    private struct LogMessage
    {
        private readonly LogLevel _level;
        private readonly string _message;

        public LogMessage(string message, LogLevel level)
        {
            _message = message;
            _level = level;
        }

        public void Log()
        {
            switch (_level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    Console.WriteLine(_message);
                    break;
                case LogLevel.Warn:
                    Console.WriteLine(_message);
                    break;
                case LogLevel.Error:
                    Console.WriteLine(_message);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static readonly TransferBuffer<LogMessage> LogsFromOtherThreads = new TransferBuffer<LogMessage>(512);
    private static Thread _main;

    public static void WriteMultithreadedLogs()
    {
        if (_main == null)
            _main = Thread.CurrentThread;

        LogMessage msg;
        while (LogsFromOtherThreads.Read(out msg))
            msg.Log();
    }

    internal static void SendLogMessage(string message, LogLevel level)
    {
#if NCRUNCH
            Console.WriteLine(message);
#else
        var msg = new LogMessage(message, level);

        if (_main == null || _main == Thread.CurrentThread)
            msg.Log();
        else
            LogsFromOtherThreads.TryWrite(msg);
#endif
    }

    #endregion

    public static LogLevel GetLogLevel()
    {
        return logLevel;
    }
}

public class NotNullAttribute : Attribute
{
}

public class Log
{
    private readonly string _traceFormat;
    private readonly string _debugFormat;
    private readonly string _basicFormat;
    private readonly string _sourceName;

    internal Log(string name, string sourceName)
    {
        _sourceName = sourceName;
        _basicFormat = (sourceName != null ? sourceName + ": " : "") + "({0:HH:mm:ss.fff}) " + name + ": {1}";
        _debugFormat = "DEBUG " + _basicFormat;
        _traceFormat = "TRACE " + _basicFormat;
    }

    public bool IsTrace
    {
        get { return ShouldLog(LogLevel.Trace); }
    }

    public bool IsDebug
    {
        get { return ShouldLog(LogLevel.Debug); }
    }

    public bool IsInfo
    {
        get { return ShouldLog(LogLevel.Info); }
    }

    public bool IsWarn
    {
        get { return ShouldLog(LogLevel.Warn); }
    }

    public bool IsError
    {
        get { return ShouldLog(LogLevel.Error); }
    }

    private bool ShouldLog(LogLevel level)
    {
        return level >= Logs.GetLogLevel();
    }

    #region Logging implementation

    private void WriteLog(LogLevel level, string message)
    {
        if (!ShouldLog(level))
            return;

        string format;
        switch (level)
        {
            case LogLevel.Trace:
                format = _traceFormat;
                break;

            case LogLevel.Debug:
                format = _debugFormat;
                break;

            case LogLevel.Info:
            case LogLevel.Warn:
            case LogLevel.Error:
                format = _basicFormat;
                break;

            default:
                throw new ArgumentOutOfRangeException("level", level, null);
        }

        Logs.SendLogMessage(string.Format(format, DateTime.UtcNow, message), level);
    }

    private void WriteLogFormat<TA>(LogLevel level, string format, [CanBeNull] TA p0)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0));
    }

    private void WriteLogFormat<TA, TB>(LogLevel level, string format, [CanBeNull] TA p0, [CanBeNull] TB p1)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1));
    }


    private void WriteLogFormat<TA, TB, TC>(LogLevel level, string format, [CanBeNull] TA p0, [CanBeNull] TB p1,
        [CanBeNull] TC p2)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1, p2));
    }


    private void WriteLogFormat<TA, TB, TC, TD>(LogLevel level, string format, [CanBeNull] TA p0, [CanBeNull] TB p1,
        [CanBeNull] TC p2, [CanBeNull] TD p3)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1, p2, p3));
    }


    private void WriteLogFormat<TA, TB, TC, TD, TE>(LogLevel level, string format, [CanBeNull] TA p0, [CanBeNull] TB p1,
        [CanBeNull] TC p2, [CanBeNull] TD p3, [CanBeNull] TE p4)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1, p2, p3, p4));
    }


    private void WriteLogFormat<TA, TB, TC, TD, TE, TF>(LogLevel level, string format, [CanBeNull] TA p0,
        [CanBeNull] TB p1, [CanBeNull] TC p2, [CanBeNull] TD p3, [CanBeNull] TE p4, [CanBeNull] TF p5)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1, p2, p3, p4, p5));
    }


    private void WriteLogFormat<TA, TB, TC, TD, TE, TF, TG>(LogLevel level, string format, [CanBeNull] TA p0,
        [CanBeNull] TB p1, [CanBeNull] TC p2, [CanBeNull] TD p3, [CanBeNull] TE p4, [CanBeNull] TF p5,
        [CanBeNull] TG p6)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1, p2, p3, p4, p5, p6));
    }


    private void WriteLogFormat<TA, TB, TC, TD, TE, TF, TG, TH>(LogLevel level, string format, [CanBeNull] TA p0,
        [CanBeNull] TB p1, [CanBeNull] TC p2, [CanBeNull] TD p3, [CanBeNull] TE p4, [CanBeNull] TF p5,
        [CanBeNull] TG p6, [CanBeNull] TH p7)
    {
        if (!ShouldLog(level))
            return;

        WriteLog(level, string.Format(format, p0, p1, p2, p3, p4, p5, p6, p7));
    }

    #endregion

    #region Trace

    public void Trace(string message)
    {
        WriteLog(LogLevel.Trace, message);
    }


    public void Trace<TA>(string format, [CanBeNull] TA p0)
    {
        WriteLogFormat(LogLevel.Trace, format, p0);
    }


    public void Trace<TA, TB>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1)
    {
        WriteLogFormat(LogLevel.Trace, format, p0, p1);
    }

    #endregion

    #region Debug

    public void Debug(string message)
    {
        WriteLog(LogLevel.Debug, message);
    }


    public void Debug<TA>(string format, [CanBeNull] TA p0)
    {
        WriteLogFormat(LogLevel.Debug, format, p0);
    }


    public void Debug<TA, TB>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1);
    }


    public void Debug<TA, TB, TC>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1, p2);
    }


    public void Debug<TA, TB, TC, TD>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1, p2, p3);
    }


    public void Debug<TA, TB, TC, TD, TE>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3, [CanBeNull] TE p4)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1, p2, p3, p4);
    }


    public void Debug<TA, TB, TC, TD, TE, TF>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3, [CanBeNull] TE p4, [CanBeNull] TF p5)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1, p2, p3, p4, p5);
    }


    public void Debug<TA, TB, TC, TD, TE, TF, TG>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1,
        [CanBeNull] TC p2, [CanBeNull] TD p3, [CanBeNull] TE p4, [CanBeNull] TF p5, [CanBeNull] TG p6)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1, p2, p3, p4, p5, p6);
    }


    public void Debug<TA, TB, TC, TD, TE, TF, TG, TH>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1,
        [CanBeNull] TC p2, [CanBeNull] TD p3, [CanBeNull] TE p4, [CanBeNull] TF p5, [CanBeNull] TG p6,
        [CanBeNull] TH p7)
    {
        WriteLogFormat(LogLevel.Debug, format, p0, p1, p2, p3, p4, p5, p6, p7);
    }

    #endregion

    #region info

    public void Info(string message)
    {
        WriteLog(LogLevel.Info, message);
    }


    public void Info<TA>(string format, [CanBeNull] TA p0)
    {
        WriteLogFormat(LogLevel.Info, format, p0);
    }


    public void Info<TA, TB>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1)
    {
        WriteLogFormat(LogLevel.Info, format, p0, p1);
    }


    public void Info<TA, TB, TC>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2)
    {
        WriteLogFormat(LogLevel.Info, format, p0, p1, p2);
    }


    public void Info<TA, TB, TC, TD>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3)
    {
        WriteLogFormat(LogLevel.Info, format, p0, p1, p2, p3);
    }


    public void Info<TA, TB, TC, TD, TE>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3, [CanBeNull] TE p4)
    {
        WriteLogFormat(LogLevel.Info, format, p0, p1, p2, p3, p4);
    }

    #endregion

    #region warn

    public void Warn(string message)
    {
        WriteLog(LogLevel.Warn, message);
    }


    public void Warn<TA>(string format, [CanBeNull] TA p0)
    {
        WriteLogFormat(LogLevel.Warn, format, p0);
    }


    public void Warn<TA, TB>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1)
    {
        WriteLogFormat(LogLevel.Warn, format, p0, p1);
    }


    public void Warn<TA, TB, TC>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2)
    {
        WriteLogFormat(LogLevel.Warn, format, p0, p1, p2);
    }


    public void Warn<TA, TB, TC, TD>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3)
    {
        WriteLogFormat(LogLevel.Warn, format, p0, p1, p2, p3);
    }


    public void Warn<TA, TB, TC, TD, TE>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2,
        [CanBeNull] TD p3, [CanBeNull] TE p4)
    {
        WriteLogFormat(LogLevel.Warn, format, p0, p1, p2, p3, p4);
    }

    #endregion

    #region error

    public void Error(string message)
    {
        WriteLog(LogLevel.Error, message);
    }


    public void Error<TA>(string format, [CanBeNull] TA p0)
    {
        WriteLogFormat(LogLevel.Error, format, p0);
    }


    public void Error<TA, TB>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1)
    {
        WriteLogFormat(LogLevel.Error, format, p0, p1);
    }


    public void Error<TA, TB, TC>(string format, [CanBeNull] TA p0, [CanBeNull] TB p1, [CanBeNull] TC p2)
    {
        WriteLogFormat(LogLevel.Error, format, p0, p1, p2);
    }

    #endregion
}

internal class CanBeNullAttribute : Attribute
{
}