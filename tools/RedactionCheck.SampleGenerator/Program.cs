using RedactionCheck.SampleGenerator;

var outDir = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "samples");
Directory.CreateDirectory(outDir);

var samples = new (string File, byte[] Content)[]
{
    ("01-填充黑框-假涂黑.pdf", SampleFileBuilder.FilledRectangle()),
    ("02-粗描边线-记号笔.pdf", SampleFileBuilder.ThickStrokeBars()),
    ("03-注释方块-遮盖.pdf", SampleFileBuilder.MarkupAnnotation()),
    ("04-不透明图片-遮盖.pdf", SampleFileBuilder.OpaqueImageCover()),
    ("05-变换与裁剪路径.pdf", SampleFileBuilder.TransformedAndClipped()),
    ("06-多页混合遮挡.pdf", SampleFileBuilder.MultiPageMixed()),
};

foreach (var (file, content) in samples)
{
    var path = Path.Combine(outDir, file);
    await File.WriteAllBytesAsync(path, content);
    Console.WriteLine($"已生成 {path}  ({content.Length:N0} 字节)");
}

Console.WriteLine($"\n共 {samples.Length} 份样例，全部为“视觉遮挡但文字仍可复制”的假涂黑文件。");
