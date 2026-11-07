namespace CacheMover;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show("程序出错：" + e.Exception.Message, "缓存搬家",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MessageBox.Show("程序出错：" + (e.ExceptionObject as Exception)?.Message, "缓存搬家",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        ApplicationConfiguration.Initialize();

        // 隐藏功能：CacheMover.exe --screenshot <输出目录>
        // 依次渲染三个页面为 PNG 后自动退出，用于生成 README 截图
        if (args.Length >= 1 && args[0] == "--screenshot")
        {
            var dir = args.Length >= 2 ? args[1] : ".";
            Application.Run(new MainForm(new[] { args[0], dir }));
            return;
        }

        Application.Run(new MainForm());
    }
}
