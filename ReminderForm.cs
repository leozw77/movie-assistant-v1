using System.Diagnostics;

namespace QbPotDoubanAi;

public sealed class ReminderForm : Form
{
    private bool _resolved;
    public ReminderForm(string title, string path, Func<bool, Task> startAi, Action done, Action snooze)
    {
        Text = "看完了，写一段评价吧"; Width = 560; Height = 260; StartPosition = FormStartPosition.CenterScreen; TopMost = true;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var ai = new Button { Text = "AI 提问并写影评", AutoSize = true };
        var reidentify = new Button { Text = "更正影片/重新识别", AutoSize = true };
        var open = new Button { Text = "直接打开豆瓣", AutoSize = true };
        var later = new Button { Text = "稍后提醒", AutoSize = true };
        var rated = new Button { Text = "已经评过", AutoSize = true };
        ai.Click += async (_, _) =>
        {
            ai.Enabled = false; ai.Text = "正在识别影片…";
            try { await startAi(false); _resolved = true; done(); Close(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "识别失败", MessageBoxButtons.OK, MessageBoxIcon.Error); ai.Enabled = true; ai.Text = "AI 提问并写影评"; }
        };
        reidentify.Click += async (_, _) =>
        {
            reidentify.Enabled = false; reidentify.Text = "正在清除并重新识别…";
            try { await startAi(true); _resolved = true; done(); Close(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "重新识别失败", MessageBoxButtons.OK, MessageBoxIcon.Error); reidentify.Enabled = true; reidentify.Text = "更正影片/重新识别"; }
        };
        open.Click += (_, _) => { _resolved = true; Process.Start(new ProcessStartInfo("https://search.douban.com/movie/subject_search?search_text=" + Uri.EscapeDataString(title)) { UseShellExecute = true }); done(); Close(); };
        later.Click += (_, _) => { _resolved = true; snooze(); Close(); }; rated.Click += (_, _) => { _resolved = true; done(); Close(); };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(500, 0) }; buttons.Controls.Add(ai); buttons.Controls.Add(reidentify); buttons.Controls.Add(open); buttons.Controls.Add(later); buttons.Controls.Add(rated);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20), AutoScroll = true };
        panel.Controls.Add(new Label { Text = title, Font = new Font(SystemFonts.DefaultFont.FontFamily, 16, FontStyle.Bold), AutoSize = true, MaximumSize = new Size(500, 0) });
        panel.Controls.Add(new Label { Text = Path.GetFileName(path), ForeColor = Color.DimGray, AutoSize = true, MaximumSize = new Size(500, 0) });
        panel.Controls.Add(new Label { Text = "爱奇艺影片已播放到结尾附近。现在生成针对这部影片的问题吗？", AutoSize = true }); panel.Controls.Add(buttons); Controls.Add(panel);
        FormClosed += (_, _) => { if (!_resolved) snooze(); };
    }
}

