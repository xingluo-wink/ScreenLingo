using ScreenLingo.Core;
namespace ScreenLingo;

public interface IReaderHost
{
 AppSettings Settings { get; }
 void SaveSettings();
 void ShowSettings();
 Task<OcrResponse> RecognizeAsync(byte[] image,CancellationToken token);
 TranslationService CreateTranslationService();
}
