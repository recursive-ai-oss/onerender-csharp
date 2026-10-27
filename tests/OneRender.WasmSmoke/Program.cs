using System;
using OneRender;
using OneRender.Fonts;

Console.WriteLine(EngineInfo.Name);
FontFaceInfo face = DefaultFonts.GetFaces("Carlito")[0];
byte[] bytes = face.ReadAllBytes();
Console.WriteLine($"{face} {bytes.Length}");
