using System.Diagnostics;

namespace QbPotDoubanAi;

public sealed class DoubanConfirmForm : Form
{
    private readonly string _searchTitle;
    private readonly TextBox _url = new() { Dock = DockStyle.Fill, Font = new Font(SystemFonts.DefaultFont.FontFamily, 10.5f) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    public MovieIdentity? Result { get; private set; }

    public DoubanConfirmForm(string searchTitle, IReadOnlyList<MovieIdentity> candidates, MovieIdentity? attempted)
    {
        _searchTitle = searchTitle; Text = "需要确认影片"; ClientSize = new Size(660, 310); MinimumSize = new Size(660, 310);
        StartPosition = FormStartPosition.CenterScreen; TopMost = true; AutoScaleMode = AutoScaleMode.Dpi;
        var open = new Button { Text = "打开豆瓣搜索", AutoSize = true };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo("https://search.douban.com/movie/subject_search?search_text=" + Uri.EscapeDataString(searchTitle)) { UseShellExecute = true });
        var paste = new Button { Text = "从剪贴板读取并确认", AutoSize = true };
        paste.Click += async (_, _) =>
        {
            if (Clipboard.ContainsText()) _url.Text = Clipboard.GetText().Trim();
            paste.Enabled = false; _status.Text = "正在读取豆瓣影片资料…";
            try { Result = await new DoubanPageService().ReadAsync(_url.Text); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) { _status.Text = ex.Message; MessageBox.Show(this, ex.Message, "读取失败", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { paste.Enabled = true; }
        };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var attemptedText = attempted is null ? "AI未返回可用结果；AI结果不会直接决定 IMDb 绑定。" : $"AI辅助结果（仅供参考）：{attempted.Title} {attempted.Year}；IMDb：{(string.IsNullOrWhiteSpace(attempted.ImdbId) ? "未识别" : attempted.ImdbId)}；可信度：{attempted.Confidence}";
        var candidateText = candidates.Count == 0
            ? "Wikipedia/Wikidata 没有通过标题一致性门槛的候选。"
            : string.Join("\n", candidates.Take(3).Select((candidate, index) =>
                $"{index + 1}. {candidate.Title} {candidate.Year}；IMDb：{(string.IsNullOrWhiteSpace(candidate.ImdbId) ? "未识别" : candidate.ImdbId)}；匹配：{candidate.MatchScore}分；{candidate.MatchEvidence}"));
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight }; buttons.Controls.Add(open); buttons.Controls.Add(paste); buttons.Controls.Add(cancel);
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20) };
        panel.Controls.Add(new Label { Text = "自动识别结果不够可靠，请手动确认一次。", AutoSize = true, Font = new Font(SystemFonts.DefaultFont.FontFamily, 12, FontStyle.Bold) });
        panel.Controls.Add(new Label { Text = "外部候选（最多显示前三项）：", AutoSize = true, Margin = new Padding(3, 8, 3, 2) });
        panel.Controls.Add(new Label { Text = candidateText, AutoSize = true, MaximumSize = new Size(610, 0), ForeColor = Color.DarkSlateGray, Margin = new Padding(3, 0, 3, 8) });
        panel.Controls.Add(new Label { Text = attemptedText, AutoSize = true, MaximumSize = new Size(610, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 12) });
        panel.Controls.Add(new Label { Text = "打开搜索并进入正确的豆瓣影片页面，复制地址栏网址：", AutoSize = true });
        _url.Width = 600; _url.PlaceholderText = "https://movie.douban.com/subject/……/"; panel.Controls.Add(_url); panel.Controls.Add(buttons); panel.Controls.Add(_status); Controls.Add(panel);
        CancelButton = cancel;
    }
}

