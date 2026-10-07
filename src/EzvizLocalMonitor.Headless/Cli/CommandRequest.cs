namespace EzvizLocalMonitor.Headless.Cli;

public sealed record CommandRequest(string Command, string? Path, IReadOnlyDictionary<string, string?> Options)
{
    public bool Has(string name) => Options.ContainsKey(name);
    public string? Get(string name) => Options.GetValueOrDefault(name);

    public static CommandRequest Parse(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["help"]) return new("help", null, new Dictionary<string, string?>());
        var index = 1;
        var command = args[0];
        string? path = null;
        if (command is "config" or "backup" or "alerts")
        {
            if (args.Length < 2) throw Usage();
            command += " " + args[1];
            index = 2;
        }
        if (command is "backup import" or "backup export")
        {
            if (index >= args.Length || args[index].StartsWith('-')) throw Usage();
            path = args[index++];
        }
        var allowed = command switch
        {
            "configure" => new[] { "--data-dir", "--stdin" },
            "backup import" or "backup export" => new[] { "--data-dir", "--password-stdin" },
            "status" => new[] { "--data-dir", "--json" },
            "alerts test" => new[] { "--data-dir", "--channel" },
            "run" or "doctor" or "config validate" => new[] { "--data-dir" },
            "version" => Array.Empty<string>(),
            _ => throw Usage()
        };
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        while (index < args.Length)
        {
            var option = args[index++];
            if (!allowed.Contains(option) || options.ContainsKey(option)) throw Usage();
            string? value = null;
            if (option is "--data-dir" or "--channel")
            {
                if (index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith('-')) throw Usage();
                value = args[index++];
            }
            options.Add(option, value);
        }
        if (command == "alerts test" && options.GetValueOrDefault("--channel") is not ("telegram" or "zalo")) throw Usage();
        return new(command, path, options);
    }

    private static ConfigurationException Usage() => new("Invalid command or option. Use --help. Secrets must use a terminal prompt or explicit stdin option, never arguments.");
}
