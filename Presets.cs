namespace CacheMover;

public sealed class Preset
{
    public Preset(string name, string path, string note)
    {
        Name = name;
        Path = path;
        Note = note;
    }

    public string Name { get; }
    public string Path { get; }
    public string Note { get; }

    public override string ToString() => Name;
}

public static class Presets
{
    private static readonly string Docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    private static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static readonly Preset[] All =
    {
        new("微信（新版 4.x）聊天记录", System.IO.Path.Combine(Docs, "xwechat_files"), "迁移前请完全退出微信，迁回时同样退出"),
        new("微信（旧版 3.x）聊天记录", System.IO.Path.Combine(Docs, "WeChat Files"), "迁移前请完全退出微信"),
        new("QQ 聊天记录", System.IO.Path.Combine(Docs, "Tencent Files"), "迁移前请完全退出 QQ"),
        new("Chrome 浏览器用户数据", System.IO.Path.Combine(Local, @"Google\Chrome\User Data"), "迁移前请完全退出 Chrome（含后台进程）"),
        new("Edge 浏览器用户数据", System.IO.Path.Combine(Local, @"Microsoft\Edge\User Data"), "迁移前请完全退出 Edge（含后台进程）"),
        new("npm 缓存", System.IO.Path.Combine(Local, "npm-cache"), "可先执行 npm cache verify 清理后再迁移"),
        new("pip 缓存", System.IO.Path.Combine(Local, @"pip\cache"), ""),
        new("NuGet 全局包", System.IO.Path.Combine(Profile, @".nuget\packages"), "迁移前请关闭 Visual Studio / dotnet 进程"),
        new("HuggingFace 模型缓存", System.IO.Path.Combine(Profile, @".cache\huggingface"), "AI 模型缓存通常很大，适合迁移"),
        new("通用 .cache 缓存目录", System.IO.Path.Combine(Profile, ".cache"), "各类 AI / 开发工具的默认缓存位置"),
        new("Gradle 缓存", System.IO.Path.Combine(Profile, ".gradle"), "迁移前请关闭 IDE 中的 Gradle 任务"),
        new("Maven 本地仓库", System.IO.Path.Combine(Profile, ".m2"), "迁移前请关闭 IDE"),
        new("Docker Desktop WSL 数据", System.IO.Path.Combine(Local, @"Docker\wsl"), "必须先完全退出 Docker Desktop，否则会自动中止"),
        new("网易云音乐缓存", System.IO.Path.Combine(Local, @"Netease\CloudMusic\Cache"), "迁移前请退出网易云音乐"),
    };
}
