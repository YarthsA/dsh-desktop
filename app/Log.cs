using System.IO;

namespace DshDesktop;

// 极简文件日志：写入 %LOCALAPPDATA%\DshDesktop\app.log，
// 记录服务启动/退出/重启与错误，供排障使用。日志失败绝不影响主流程。
internal static class Log
{
    private const long MaxFileBytes = 1_000_000; // 超过 1MB 轮转为 app.log.old

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DshDesktop", "app.log");

    private static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO", msg);

    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                RotateIfNeeded();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志写不进去时静默，不影响主流程
        }
    }

    private static void RotateIfNeeded()
    {
        var fi = new FileInfo(FilePath);
        if (!fi.Exists || fi.Length <= MaxFileBytes) return;
        var old = FilePath + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(FilePath, old);
    }
}
