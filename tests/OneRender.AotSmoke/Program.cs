using System;
using OneRender;
using OneRender.Image;
using OneRender.Pdf;

string[] names = [EngineInfo.Name, ImageBackendInfo.Name, PdfBackendInfo.Name];
Console.WriteLine(string.Join(' ', names));
// The provisional geometry entry point must survive trimming and AOT as well.
int boxes = GeometryProbe.LayOut("<p>x</p>", null, null, 800, 600).Count;
return names.Length == 3 && boxes == 0 ? 0 : 1;
