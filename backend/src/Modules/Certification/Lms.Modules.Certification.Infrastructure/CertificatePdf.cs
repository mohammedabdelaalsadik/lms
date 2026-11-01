using Lms.Modules.Certification.Application;
using Lms.Modules.Certification.Domain;
using Microsoft.Extensions.Configuration;
using QRCoder;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using System.Globalization;

namespace Lms.Modules.Certification.Infrastructure;

/// <summary>
/// Draws one A4 landscape page. HarfBuzz joins Arabic letters but does not reorder mixed text,
/// so each line is split into Arabic and non-Arabic runs and placed here.
/// </summary>
internal sealed class SkiaCertificatePdf(IConfiguration configuration) : ICertificatePdf
{
    private const float PageWidth = 842;
    private const float PageHeight = 595;
    private const float LeftColumn = 235;
    private const float RightColumn = 607;

    private static readonly SKColor Brand = SKColor.Parse("#0083A0");
    private static readonly SKColor Ink = SKColor.Parse("#06384A");
    private static readonly SKColor Accent = SKColor.Parse("#B7791F");
    private static readonly SKColor Quiet = SKColor.Parse("#4F6470");

    private static readonly SKTypeface Regular = LoadFont("Regular");
    private static readonly SKTypeface Bold = LoadFont("Bold");

    private readonly string _verifyBase = (configuration["Certification:VerifyBaseUrl"]
        ?? throw new InvalidOperationException("Certification:VerifyBaseUrl is required.")).TrimEnd('/');

    public byte[] Render(Certificate certificate)
    {
        using var output = new MemoryStream();
        var metadata = new SKDocumentPdfMetadata
        {
            Title = $"Certificate {certificate.Code}",
            Creator = "Training Platform",
        };

        using (var document = SKDocument.CreatePdf(output, metadata))
        {
            var canvas = document.BeginPage(PageWidth, PageHeight);
            DrawFrame(canvas);
            DrawBody(canvas, certificate);
            DrawFooter(canvas, certificate);

            if (certificate.Status == CertificateStatus.Revoked)
            {
                DrawRevokedStamp(canvas);
            }

            document.EndPage();
            document.Close();
        }

        return output.ToArray();
    }

    private static SKTypeface LoadFont(string weight)
    {
        var assembly = typeof(SkiaCertificatePdf).Assembly;
        var name = assembly.GetManifestResourceNames().First(n => n.EndsWith($"IBMPlexSansArabic-{weight}.ttf", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data);
    }

    private static void DrawFrame(SKCanvas canvas)
    {
        using var band = new SKPaint { Color = Brand, IsAntialias = true };
        using var accent = new SKPaint { Color = Accent, IsAntialias = true };
        using var line = new SKPaint { Color = Ink.WithAlpha(60), IsStroke = true, StrokeWidth = 0.75f, IsAntialias = true };

        canvas.DrawRect(0, 0, PageWidth, 14, band);
        canvas.DrawRect(0, 14, PageWidth, 3, accent);
        canvas.DrawRect(0, PageHeight - 17, PageWidth, 3, accent);
        canvas.DrawRect(0, PageHeight - 14, PageWidth, 14, band);
        canvas.DrawLine(PageWidth / 2, 165, PageWidth / 2, 415, line);
    }

    private static void DrawBody(SKCanvas canvas, Certificate c)
    {
        Centered(canvas, "شهادة إتمام", PageWidth / 2, 78, Bold, 30, Ink);
        Centered(canvas, "Certificate of Completion", PageWidth / 2, 115, Regular, 20, Quiet);

        Centered(canvas, "This certifies that", LeftColumn, 195, Regular, 13, Quiet);
        Fitted(canvas, c.TraineeNameEn, LeftColumn, 238, Bold, 24, Ink);
        Centered(canvas, "has successfully completed", LeftColumn, 285, Regular, 13, Quiet);
        Fitted(canvas, c.CourseTitleEn, LeftColumn, 328, Bold, 19, Ink);
        Centered(canvas, c.CourseCode, LeftColumn, 358, Regular, 12, Accent);

        Centered(canvas, "تشهد المنصة بأن", RightColumn, 195, Regular, 13, Quiet);
        Fitted(canvas, c.TraineeNameAr, RightColumn, 238, Bold, 24, Ink);
        Centered(canvas, "قد أتم بنجاح", RightColumn, 285, Regular, 13, Quiet);
        Fitted(canvas, c.CourseTitleAr, RightColumn, 328, Bold, 19, Ink);
        Centered(canvas, c.CourseCode, RightColumn, 358, Regular, 12, Accent);
    }

    private void DrawFooter(SKCanvas canvas, Certificate c)
    {
        var date = c.CompletedOn.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("en-GB"));
        var url = $"{_verifyBase}/verify/{c.Code}";

        Left(canvas, "Completed on", 70, 462, Regular, 11, Quiet);
        Left(canvas, date, 70, 482, Bold, 14, Ink);
        Left(canvas, "Verification code", 70, 510, Regular, 11, Quiet);
        Left(canvas, c.Code, 70, 530, Bold, 14, Ink);

        DrawQrCode(canvas, url, 726, 446, 84);
    }

    private static void DrawQrCode(SKCanvas canvas, string text, float x, float y, float size)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var cell = size / matrix.Count;

        using var paint = new SKPaint { Color = Ink, IsAntialias = false };

        for (var row = 0; row < matrix.Count; row++)
        {
            for (var column = 0; column < matrix.Count; column++)
            {
                if (matrix[row][column])
                {
                    canvas.DrawRect(x + (column * cell), y + (row * cell), cell + 0.1f, cell + 0.1f, paint);
                }
            }
        }
    }

    private static void DrawRevokedStamp(SKCanvas canvas)
    {
        canvas.Save();
        canvas.RotateDegrees(-20, PageWidth / 2, PageHeight / 2);
        Centered(canvas, "REVOKED", PageWidth / 2, PageHeight / 2, Bold, 90, new SKColor(180, 30, 30, 90));
        canvas.Restore();
    }

    /// <summary>Long names shrink to stay inside their half of the page.</summary>
    private static void Fitted(SKCanvas canvas, string text, float centerX, float y, SKTypeface typeface, float size, SKColor color)
    {
        const float maxWidth = 330;
        var width = Measure(text, typeface, size);
        var fitted = width > maxWidth ? Math.Max(9, size * maxWidth / width) : size;
        Centered(canvas, text, centerX, y, typeface, fitted, color);
    }

    private static void Centered(SKCanvas canvas, string text, float centerX, float y, SKTypeface typeface, float size, SKColor color) =>
        Draw(canvas, text, centerX - (Measure(text, typeface, size) / 2), y, typeface, size, color);

    private static void Left(SKCanvas canvas, string text, float x, float y, SKTypeface typeface, float size, SKColor color) =>
        Draw(canvas, text, x, y, typeface, size, color);

    private static float Measure(string text, SKTypeface typeface, float size)
    {
        using var font = new SKFont(typeface, size);
        using var shaper = new SKShaper(typeface);
        return VisualOrder(text).Sum(run => shaper.Shape(run, font).Width);
    }

    private static void Draw(SKCanvas canvas, string text, float x, float y, SKTypeface typeface, float size, SKColor color)
    {
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        using var shaper = new SKShaper(typeface);

        foreach (var run in VisualOrder(text))
        {
            canvas.DrawShapedText(shaper, run, x, y, SKTextAlign.Left, font, paint);
            x += shaper.Shape(run, font).Width;
        }
    }

    /// <summary>
    /// An Arabic line reads right to left, so its runs are laid out reversed, and a Latin run's
    /// trailing space moves to its left to keep the gap between it and the next word.
    /// </summary>
    private static IEnumerable<string> VisualOrder(string text)
    {
        var runs = Runs(text);

        if (!runs.Exists(IsArabic))
        {
            return runs;
        }

        return Enumerable.Reverse(runs).Select(run =>
        {
            var trimmed = run.TrimEnd();
            return IsArabic(run) ? run : new string(' ', run.Length - trimmed.Length) + trimmed;
        });
    }

    private static bool IsArabic(string run) => run.Any(IsArabicChar);

    private static bool IsArabicChar(char c) =>
        c is (>= '؀' and <= 'ۿ') or (>= 'ﭐ' and <= '﷿') or (>= 'ﹰ' and <= '﻿');

    /// <summary>Splits text where it changes script. Spaces and punctuation stay with the run before them.</summary>
    private static List<string> Runs(string text)
    {
        var runs = new List<string>();
        var start = 0;
        bool? arabic = null;

        for (var i = 0; i < text.Length; i++)
        {
            if (!char.IsLetterOrDigit(text[i]))
            {
                continue;
            }

            var current = IsArabicChar(text[i]);

            if (arabic is not null && current != arabic)
            {
                runs.Add(text[start..i]);
                start = i;
            }

            arabic = current;
        }

        runs.Add(text[start..]);
        return runs;
    }
}
