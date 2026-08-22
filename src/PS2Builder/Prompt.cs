namespace PS2Builder;

public static class Prompt
{
    public static string? Show(string title, string text)
    {
        using var f = new Form { Text = title, Width = 520, Height = 165, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Text = text, Left = 12, Top = 12, Width = 475 };
        var box = new TextBox { Left = 12, Top = 40, Width = 475, UseSystemPasswordChar = true };
        var ok = new Button { Text = "OK", Left = 330, Top = 75, Width = 75, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Annulla", Left = 412, Top = 75, Width = 75, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange([label, box, ok, cancel]); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? box.Text : null;
    }
}
