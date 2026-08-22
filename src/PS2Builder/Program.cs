namespace PS2Builder;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var root = AppContext.BaseDirectory;
        var manifest = Path.Combine(root, ".ps2builder", "manifest.json");
        var playMode = args.Any(a => a.Equals("--play", StringComparison.OrdinalIgnoreCase)) || File.Exists(manifest);
        if (playMode)
        {
            try { Player.Run(root); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "PS2 Builder - Play", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            return;
        }
        Application.Run(new MainForm());
    }
}
