using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.Annotations;
using PdfSharpCore.Pdf.IO;
using RedactionCheck.Core.Geometry;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using AddedFont = UglyToad.PdfPig.Writer.PdfDocumentBuilder.AddedFont;
using PigRectangle = UglyToad.PdfPig.Core.PdfRectangle;
using SharpRectangle = PdfSharpCore.Pdf.PdfRectangle;

namespace RedactionCheck.SampleGenerator;

/// <summary>
/// Builds six PDFs that mimic the ways legal teams "black out" content without
/// actually removing it. Every generated file keeps its text fully extractable
/// underneath the visual cover, which is exactly what the scanner must catch.
/// </summary>
public static class SampleFileBuilder
{
    private const double PageW = 595;   // A4 portrait, points
    private const double PageH = 842;
    private const double Left = 72;
    private const double BoxW = 360;

    // Baselines produced by AddLines (heading first, then 30pt line spacing).
    private static readonly double[] Baselines =
        { PageH - 110, PageH - 140, PageH - 170, PageH - 200, PageH - 230 };
    private const double BoxBottomOffset = 4;   // box extends 4pt below baseline
    private const double BoxHeight = 16;        // and 12pt above, covering 12pt glyphs

    // Arial Unicode covers the Chinese sample text; Arial Bold gives us a
    // distinct heading face. Both are embedded as TrueType subsets by PdfPig.
    private static readonly string CjkFontPath = "/System/Library/Fonts/Supplemental/Arial Unicode.ttf";
    private static readonly string BoldFontPath = "/System/Library/Fonts/Supplemental/Arial Bold.ttf";

    private static AddedFont LoadCjk(PdfDocumentBuilder b) =>
        b.AddTrueTypeFont(File.ReadAllBytes(CjkFontPath));
    private static AddedFont LoadBold(PdfDocumentBuilder b) =>
        b.AddTrueTypeFont(File.ReadAllBytes(BoldFontPath));

    /// <summary>1. Opaque black filled rectangle painted on top of live text.</summary>
    public static byte[] FilledRectangle()
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageW, PageH);
        var font = LoadCjk(builder);
        var bold = LoadCjk(builder);

        AddLines(page, font, bold, new (string, double)[]
        {
            ("保密协议 — 仅供内部评审", 14),
            ("甲方银行账号：6222 0212 3456 7890 123", 12),
            ("客户身份证号：110101199003077758", 12),
            ("对接人手机号：138-0013-8000", 12),
            ("以下为公开条款，无敏感信息。", 12),
        });

        page.SetTextAndFillColor(0, 0, 0);
        // Sensitive body lines are indices 1..3 (heading 0 stays visible).
        foreach (var line in new[] { 1, 2, 3 })
            page.DrawRectangle(new PdfPoint(Left, Baselines[line] - BoxBottomOffset),
                BoxW, BoxHeight, 0.5, fill: true);

        return BuildToBytes(builder);
    }

    /// <summary>2. Wide stroked lines ("black marker pen") over the text.</summary>
    public static byte[] ThickStrokeBars()
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageW, PageH);
        var font = LoadCjk(builder);
        AddLines(page, font, font, new (string, double)[]
        {
            ("薪资构成说明（涉密）", 14),
            ("年度奖金池：人民币 8,400,000 元", 12),
            ("期权授予数量：120,000 股", 12),
            ("本页其余内容可以对外披露。", 12),
        });

        page.SetStrokeColor(0, 0, 0);
        // 16pt pen centred on the baseline covers the full 12pt glyph height.
        foreach (var line in new[] { 1, 2 })
            page.DrawLine(new PdfPoint(Left, Baselines[line]),
                new PdfPoint(Left + BoxW, Baselines[line]), 16);

        return BuildToBytes(builder);
    }

    /// <summary>3. Square markup annotation placed over text by a reviewer.</summary>
    public static byte[] MarkupAnnotation()
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageW, PageH);
        var font = LoadCjk(builder);
        AddLines(page, font, font, new (string, double)[]
        {
            ("证人证言摘要", 14),
            ("证人真实姓名：张伟（化名：张三）", 12),
            ("家庭住址：北京市朝阳区建国路 88 号", 12),
            ("以上信息申请不公开审理。", 12),
        });
        var basePdf = BuildToBytes(builder);

        using var ms = new MemoryStream(basePdf);
        using var doc = PdfSharpCore.Pdf.IO.PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        var p = doc.Pages[0];
        foreach (var (line, label) in new (int, string)[]
                 { (1, "姓名遮盖"), (2, "住址遮盖") })
        {
            var bottom = Baselines[line] - BoxBottomOffset;
            // A filled Square annotation renders an opaque box but never touches
            // the underlying text stream — the classic reviewer mistake.
            var annot = new PdfTextAnnotation
            {
                Rectangle = new SharpRectangle(
                    new PdfSharpCore.Drawing.XPoint(Left, bottom),
                    new PdfSharpCore.Drawing.XPoint(Left + BoxW, bottom + BoxHeight)),
                Title = "法务",
                Contents = label,
                Color = PdfSharpCore.Drawing.XColor.FromKnownColor(PdfSharpCore.Drawing.XKnownColor.Black)
            };
            annot.Elements.SetName("/Subtype", "/Square");
            annot.Elements.SetInteger("/F", 4); // print flag
            p.Annotations.Add(annot);
        }
        using var outMs = new MemoryStream();
        doc.Save(outMs, false);
        return outMs.ToArray();
    }

    /// <summary>4. Opaque raster image XObject painted over the text.</summary>
    public static byte[] OpaqueImageCover()
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageW, PageH);
        var font = LoadCjk(builder);
        AddLines(page, font, font, new (string, double)[]
        {
            ("并购尽调报告 — 机密", 14),
            ("标的公司未披露负债：3.2 亿元", 12),
            ("潜在诉讼涉案金额：9,800 万元", 12),
            ("报告结论：建议继续推进。", 12),
        });

        var png = BuildBlackPng();
        foreach (var line in new[] { 1, 2 })
        {
            var bottom = Baselines[line] - BoxBottomOffset;
            // PdfRectangle normalises corner order; the image scales to this box.
            page.AddPng(png, new PigRectangle(Left, bottom, Left + BoxW, bottom + BoxHeight));
        }
        return BuildToBytes(builder);
    }

    /// <summary>
    /// 5. Filled box applied through a cm transform + clipping path, exercising
    /// CTM tracking and transformed-coordinate redaction.
    /// </summary>
    public static byte[] TransformedAndClipped()
    {
        using var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageW, PageH);
        var font = LoadCjk(builder);
        AddLines(page, font, font, new (string, double)[]
        {
            ("仲裁条款附件", 14),
            ("仲裁员候选名单：李某、王某、赵某", 12),
            ("仲裁账户托管余额：USD 250,000", 12),
            ("附件结束。", 12),
        });

        // Raw operators: clip, transform, fill — kept as a separate overlay so
        // the scanner must follow the CTM to locate the cover.
        // After cm, unit rect (0,0,360,30) maps to y 668..701, i.e. the two
        // sensitive baselines (672 and 702). Clip path is kept deliberately loose.
        var ops = new System.Text.StringBuilder();
        ops.AppendLine("q");
        ops.Append(FormattableString.Invariant($"{Left - 12:0.###} {Baselines[2] - 12:0.###} {BoxW + 24:0.###} 60 re W n\n"));
        ops.Append(FormattableString.Invariant($"1 0 0 1.1 {Left:0.###} {Baselines[2] - 4:0.###} cm\n"));
        ops.AppendLine("0 0 0 rg");
        ops.AppendLine("0 0 360 30 re f");
        ops.AppendLine("Q");
        return AppendRawOverlay(builder, page, ops.ToString());
    }

    /// <summary>6. Three pages: filled box, a clean page, then stroke bars.</summary>
    public static byte[] MultiPageMixed()
    {
        using var builder = new PdfDocumentBuilder();
        var font = LoadCjk(builder);

        var p1 = builder.AddPage(PageW, PageH);
        AddLines(p1, font, font, new (string, double)[]
        {
            ("第 1 页：填充框遮挡", 14),
            ("服务器 root 密码：P@ssw0rd-2026!", 12),
            ("备用密钥：AKIA9X8Y7Z6W5V4U3T2S", 12),
        });
        p1.SetTextAndFillColor(0, 0, 0);
        foreach (var line in new[] { 1, 2 })
            p1.DrawRectangle(new PdfPoint(Left, Baselines[line] - BoxBottomOffset),
                BoxW, BoxHeight, 0.5, fill: true);

        var p2 = builder.AddPage(PageW, PageH);
        AddLines(p2, font, font, new (string, double)[]
        {
            ("第 2 页：公开信息（无遮挡）", 14),
            ("本页所有内容均已在官网披露。", 12),
            ("联系人：前台 010-00000000", 12),
        });

        var p3 = builder.AddPage(PageW, PageH);
        AddLines(p3, font, font, new (string, double)[]
        {
            ("第 3 页：粗描边遮挡", 14),
            ("未公开收购对价：每股 42.80 元", 12),
            ("对赌净利润目标：5.6 亿元", 12),
        });
        p3.SetStrokeColor(0, 0, 0);
        foreach (var line in new[] { 1, 2 })
            p3.DrawLine(new PdfPoint(Left, Baselines[line]),
                new PdfPoint(Left + BoxW, Baselines[line]), 14);

        return BuildToBytes(builder);
    }

    // ---------- helpers ----------

    private static void AddLines(PdfPageBuilder page, AddedFont font, AddedFont bold,
        (string Text, double Size)[] lines)
    {
        var y = PageH - 110;
        for (var i = 0; i < lines.Length; i++)
        {
            var (text, size) = lines[i];
            var f = i == 0 ? bold : font;
            page.AddText(text, size, new PdfPoint(Left, y), f);
            y -= 30;
        }
    }

    private static byte[] AppendRawOverlay(PdfDocumentBuilder builder, PdfPageBuilder page, string rawOperators)
    {
        var baseBytes = builder.Build();
        using var baseMs = new MemoryStream(baseBytes);
        using var doc = PdfReader.Open(baseMs, PdfDocumentOpenMode.Modify);
        var target = doc.Pages[0];
        var overlay = target.Contents.AppendContent();
        overlay.CreateStream(System.Text.Encoding.UTF8.GetBytes(rawOperators));
        using var outMs = new MemoryStream();
        doc.Save(outMs, false);
        return outMs.ToArray();
    }

    private static byte[] BuildToBytes(PdfDocumentBuilder builder) => builder.Build();

    private static byte[] BuildBlackPng()
    {
        // Solid 1x1 black PNG; the AddPng placement rect scales it to the cover.
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    }
}
