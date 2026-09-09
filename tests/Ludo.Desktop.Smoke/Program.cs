using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ludo.Warzone;

internal static class Program
{
 [STAThread]
 private static int Main(string[] args)
 {
  var app = new Application();
  var window = new MainWindow();
  window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
  var root = (FrameworkElement)window.Content;
  root.Measure(new Size(1250, 770)); root.Arrange(new Rect(0, 0, 1250, 770)); root.UpdateLayout();
  if (window.FindName("TurnText") is not TextBlock text || !text.Text.Contains("Yellow")) throw new InvalidOperationException("Warzone did not initialize the match.");
  var visual = new DrawingVisual();
  using (var drawing = visual.RenderOpen())
  {
   drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1250, 770));
   drawing.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, 1250, 770));
  }
  var bitmap = new RenderTargetBitmap(1250, 770, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
  string output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/warzone.png"); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
  var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
  using (var file = File.Create(output)) encoder.Save(file);
  window.Close(); app.Shutdown();
  Console.WriteLine("PASS: Warzone initializes its match and renders the supplied board and piece assets.");
  return 0;
 }
}
