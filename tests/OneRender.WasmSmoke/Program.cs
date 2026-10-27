using System;
using OneRender;
using OneRender.Fonts;
using OneRender.Image;
using OneRender.Painting;

Console.WriteLine(EngineInfo.Name);
FontFaceInfo face = DefaultFonts.GetFaces("Carlito")[0];
byte[] bytes = face.ReadAllBytes();
Console.WriteLine($"{face} {bytes.Length}");

// The rasterizer must trim and run on WebAssembly too.
DisplayList list = new();
list.FillRoundedRect(ItemId.Of(1), new RectF(2, 2, 28, 28), CornerRadii.Uniform(8), new PaintColor(1f, 0f, 0f, 1f));
RgbaImage image = DisplayListRasterizer.Render(list, 32, 32);
Console.WriteLine($"rasterized {image.Width}x{image.Height} red={image.Pixels[((16 * 32) + 16) * 4]}");
