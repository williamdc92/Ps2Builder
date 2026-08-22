namespace PS2Builder;

public sealed class MainForm : Form
{
    readonly TextBox game = new() { Dock = DockStyle.Fill };
    readonly TextBox bios = new() { Dock = DockStyle.Fill };
    readonly TextBox title = new() { Dock = DockStyle.Fill };
    readonly TextBox output = new() { Dock = DockStyle.Fill };
    readonly ComboBox resolution = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox aspect = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckedListBox patches = new() { Dock = DockStyle.Fill, Height = 130 };
    readonly Label detected = new() { AutoSize = true, Text = "Carica un gioco per il riconoscimento automatico." };
    readonly Button build = new() { Text = "BUILD ISO", Dock = DockStyle.Fill, Height = 44 };
    readonly ProgressBar progress = new() { Dock = DockStyle.Fill };
    GameInfo? gameInfo;

    public MainForm()
    {
        Text = "PS2 Builder"; Width = 760; Height = 680; StartPosition = FormStartPosition.CenterScreen;
        resolution.Items.AddRange(Enum.GetNames<ResolutionProfile>()); resolution.SelectedItem = ResolutionProfile.Automatic.ToString();
        aspect.Items.AddRange(new[] { "Automatic", "Original 4:3", "Widescreen 16:9" }); aspect.SelectedIndex = 0;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 3, RowCount = 12 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        AddRow(grid, 0, "Gioco", game, BrowseButton("Gioco", game, "Immagini disco|*.iso;*.bin;*.img|Tutti i file|*.*", async () => await DetectAsync()));
        AddRow(grid, 1, "BIOS PS2", bios, BrowseButton("BIOS", bios, "BIOS|*.bin;*.rom|Tutti i file|*.*"));
        AddRow(grid, 2, "Nome disco", title, null);
        grid.Controls.Add(new Label { Text = "Riconoscimento", AutoSize = true }, 0, 3); grid.Controls.Add(detected, 1, 3); grid.SetColumnSpan(detected, 2);
        AddRow(grid, 4, "Risoluzione interna", resolution, null);
        AddRow(grid, 5, "Formato", aspect, null);
        grid.Controls.Add(new Label { Text = "Patch disponibili", AutoSize = true }, 0, 6); grid.Controls.Add(patches, 1, 6); grid.SetColumnSpan(patches, 2);
        var iconPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var iconBtn = new Button { Text = "Da file...", AutoSize = true }; iconBtn.Click += (_,__) => ChooseIcon();
        var sgdbBtn = new Button { Text = "SteamGridDB...", AutoSize = true }; sgdbBtn.Click += async (_,__) => await ChooseSteamGridIconAsync();
        iconPanel.Controls.Add(iconBtn); iconPanel.Controls.Add(sgdbBtn);
        grid.Controls.Add(new Label { Text = "Icona", AutoSize = true }, 0, 7); grid.Controls.Add(iconPanel, 1, 7);
        AddRow(grid, 8, "Output ISO", output, BrowseSaveButton());
        grid.Controls.Add(progress, 1, 9); grid.SetColumnSpan(progress, 2);
        grid.Controls.Add(build, 1, 10); grid.SetColumnSpan(build, 2);
        var note = new Label { AutoSize = true, MaximumSize = new Size(650, 0), Text = "Il disco generato contiene PLAY.exe + runtime PCSX2 + BIOS + gioco. Sul PC destinatario non serve installare PCSX2 o PS2 Builder." };
        grid.Controls.Add(note, 0, 11); grid.SetColumnSpan(note, 3);
        Controls.Add(grid);
        build.Click += async (_,__) => await BuildAsync();
    }

    static void AddRow(TableLayoutPanel p, int row, string label, Control c, Control? third)
    { p.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); p.Controls.Add(c, 1, row); if (third != null) p.Controls.Add(third, 2, row); }

    Button BrowseButton(string caption, TextBox target, string filter, Func<Task>? after = null)
    { var b = new Button { Text = "Sfoglia" }; b.Click += async (_,__) => { using var d = new OpenFileDialog { Title = caption, Filter = filter }; if (d.ShowDialog() == DialogResult.OK) { target.Text = d.FileName; if (after != null) await after(); } }; return b; }

    Button BrowseSaveButton()
    { var b = new Button { Text = "Sfoglia" }; b.Click += (_,__) => { using var d = new SaveFileDialog { Filter = "ISO|*.iso", FileName = string.IsNullOrWhiteSpace(title.Text) ? "PS2Game.iso" : SafeName(title.Text) + ".iso" }; if (d.ShowDialog() == DialogResult.OK) output.Text = d.FileName; }; return b; }

    string? iconPath;
    void ChooseIcon() { using var d = new OpenFileDialog { Filter = "Immagini|*.ico;*.png;*.jpg;*.jpeg" }; if (d.ShowDialog() == DialogResult.OK) iconPath = d.FileName; }

    async Task DetectAsync()
    {
        try
        {
            detected.Text = "Analisi..."; patches.Items.Clear();
            gameInfo = await GameDatabase.DetectAsync(game.Text);
            title.Text = gameInfo.Title;
            detected.Text = $"{gameInfo.Title} · {gameInfo.Serial} · {gameInfo.Region}" + (gameInfo.Crc is null ? "" : $" · CRC {gameInfo.Crc}");
            foreach (var p in gameInfo.Patches) patches.Items.Add(p, p.Recommended);
            if (string.IsNullOrWhiteSpace(output.Text)) output.Text = Path.Combine(Path.GetDirectoryName(game.Text)!, SafeName(gameInfo.Title) + " - PS2Builder.iso");
        }
        catch (Exception ex) { detected.Text = "Non riconosciuto: " + ex.Message; }
    }

    async Task BuildAsync()
    {
        if (!File.Exists(game.Text) || !File.Exists(bios.Text) || string.IsNullOrWhiteSpace(output.Text)) { MessageBox.Show("Seleziona gioco, BIOS e output."); return; }
        build.Enabled = false; progress.Style = ProgressBarStyle.Marquee;
        try
        {
            gameInfo ??= await GameDatabase.DetectAsync(game.Text);
            var selected = patches.CheckedItems.Cast<PatchGroupInfo>().Select(p => p.Name).ToList();
            var settings = new BuildSettings {
                GamePath = game.Text, BiosPath = bios.Text, OutputIso = output.Text,
                DisplayName = string.IsNullOrWhiteSpace(title.Text) ? gameInfo.Title : title.Text,
                CustomIconPath = iconPath,
                Resolution = Enum.Parse<ResolutionProfile>(resolution.SelectedItem!.ToString()!),
                Aspect = aspect.SelectedIndex switch { 1 => AspectProfile.Original4x3, 2 => AspectProfile.Widescreen16x9, _ => AspectProfile.Automatic },
                EnabledPatchGroups = selected
            };
            await DiscBuilder.BuildAsync(settings, gameInfo);
            MessageBox.Show("ISO creata:\n" + output.Text, "PS2 Builder", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.ToString(), "Build fallita", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { progress.Style = ProgressBarStyle.Blocks; build.Enabled = true; }
    }

    async Task ChooseSteamGridIconAsync()
    {
        if (string.IsNullOrWhiteSpace(title.Text)) { MessageBox.Show("Riconosci prima il gioco."); return; }
        var key = Prompt.Show("SteamGridDB API key", "Inserisci la tua API key SteamGridDB (non viene salvata):");
        if (string.IsNullOrWhiteSpace(key)) return;
        try
        {
            var urls = await SteamGridDb.FindIconUrlsAsync(title.Text, key);
            if (urls.Count == 0) { MessageBox.Show("Nessuna icona trovata."); return; }
            var temp = Path.Combine(Path.GetTempPath(), "ps2builder-art-" + Guid.NewGuid().ToString("N") + Path.GetExtension(new Uri(urls[0]).AbsolutePath));
            using var http = new HttpClient(); await File.WriteAllBytesAsync(temp, await http.GetByteArrayAsync(urls[0])); iconPath = temp;
            MessageBox.Show("Icona SteamGridDB selezionata automaticamente. Puoi sostituirla con 'Da file...'.");
        }
        catch (Exception ex) { MessageBox.Show("SteamGridDB: " + ex.Message); }
    }

    static string SafeName(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
}
