using System.Text.Json;
using Ludo.Runner;

namespace Ludo.Training;

public static class TrainingFiles
{
    public static async Task SaveAsync(string path, TrainingCheckpoint checkpoint)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await Atomic(path, JsonSerializer.Serialize(checkpoint.Best, RunnerJson.Options));
        await Atomic(Path.ChangeExtension(path, ".latest.json"), JsonSerializer.Serialize(checkpoint.Latest, RunnerJson.Options));
        await Atomic(Path.ChangeExtension(path, ".training.json"), JsonSerializer.Serialize(checkpoint, TrainingJsonContext.Default.TrainingCheckpoint));
    }
    private static async Task Atomic(string path, string content)
    {
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, path, true);
    }
}
