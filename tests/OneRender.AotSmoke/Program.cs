using System;
using OneRender;
using OneRender.Image;
using OneRender.Pdf;

string[] names = [EngineInfo.Name, ImageBackendInfo.Name, PdfBackendInfo.Name];
Console.WriteLine(string.Join(' ', names));
return names.Length == 3 ? 0 : 1;
