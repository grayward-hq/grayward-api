namespace Web.Bootstrap
{
    public class EnvironmentLoader
    {
        public static void LoadDotEnv()
        {
            foreach (var envPath in ResolveDotEnvCandidates())
            {
                if (!File.Exists(envPath))
                    continue;

                foreach (var rawLine in File.ReadAllLines(envPath))
                {
                    var line = rawLine.Trim();

                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                        continue;

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim();

                    if (value.Length >= 2 &&
                        ((value.StartsWith('"') && value.EndsWith('"')) ||
                         (value.StartsWith('\'') && value.EndsWith('\''))))
                    {
                        value = value[1..^1];
                    }

                    Environment.SetEnvironmentVariable(key, value);
                }

                return;
            }
        }

        static IEnumerable<string> ResolveDotEnvCandidates()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var appBaseDirectory = AppContext.BaseDirectory;

            return new[]
            {
        Path.GetFullPath(Path.Combine(currentDirectory, "api-dotnet", ".env")),
        Path.GetFullPath(Path.Combine(currentDirectory, ".env")),
        Path.GetFullPath(Path.Combine(currentDirectory, "..", "..", ".env")),
        Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", ".env")),
        Path.GetFullPath(Path.Combine(appBaseDirectory, "..", "..", "..", "..", "..", ".env"))
    }.Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
