using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using SamplePlugin.Windows;
using System;
using System.Linq;

namespace SamplePlugin
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Test Plugin Please Ignore";
        private const string CommandName = "/go";

        private DalamudPluginInterface PluginInterface { get; init; }
        private ICommandManager CommandManager { get; init; }
        public Configuration Configuration { get; init; } = new();
        [PluginService] public static IChatGui Chat { get; set; } = null!;
        [PluginService] public static IAetheryteList AetheryteList { get; set; } = null!;

        private ConfigWindow ConfigWindow { get; init; }

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] ICommandManager commandManager)
        {
            PluginInterface = pluginInterface;
            CommandManager = commandManager;
            ConfigWindow = new ConfigWindow(this);
            PluginInterface.UiBuilder.OpenConfigUi += openConfigMenu;

            _ = CommandManager.AddHandler(CommandName, new CommandInfo(onCommand)
            {
                HelpMessage = "Opens the config menu"
                + $"\n/go <location> → Teleports to a matching location."
                + $"\n"
                + $"\nExample usage:"
                + $"\n· /go limsa → Limsa Lominsa Lower Decks"
                + $"\n"
                + $"\nFeatures:"
                + $"\n· Matches against aetheryte or zones"
                + $"\n  · /go sum → Summerford Farms in Middle La Noscea"
                + $"\n  · /go azys → Helix in Azys Lla"
                + $"\n· Word prefixes are prioritized"
                + $"\n  · /go old → Old Sharlayan"
                + $"\n  · /go ost → Ostall Imperative in Lakeland"
                + $"\n· Multi-word support"
                + $"\n  · /go c shroud → Bentbranch Meadows in Central Shroud"
                + $"\n  · /go n than → Camp Bluefog in Northern Thanalan"
                + $"\n· Common prefixes ('the', 'camp') are ignored"
                + $"\n  · /go dry → Camp Drybone in Eastern Thanalan"
                + $"\n· Will match against 'Ishgard'"
                + $"\n  · /go ish → Foundation"
                + $"\n"
                + $"\nSupported flags:"
                + $"\nVerbose: --verbose or -v"
                + $"\n\t\tDisplays extended information in chat. Overrides config."
                + $"\nInfo: --info or -i"
                + $"\n\t\tDisplays basic plugin output in chat. Overrides config."
                + $"\nError: --error or -e"
                + $"\n\t\tDisplays plugin error output in chat. Overrides config."
                + $"\nSilent: --silent or -s"
                + $"\n\t\tWill not show output in chat. Overrides config."
            });
        }

        public void Dispose() { }

        private void openConfigMenu()
        {
            ConfigWindow.IsOpen = true;
        }

        private sealed class GoConfig
        {
            // Actual search value
            public string Search { get; set; } = "";

            // Log level options
            public bool LogLevelVerbose { get; set; } = false;
            public bool LogLevelInfo { get; set; } = true;
            public bool LogLevelError { get; set; } = true;

            // Logging functions
            public void LogVerbose(string message)
            {
                if (LogLevelVerbose == false)
                {
                    return;
                }

                Chat.Print(message);
            }

            public void LogInfo(string message)
            {
                if (LogLevelInfo == false)
                {
                    return;
                }

                Chat.Print(message);
            }

            public void LogError(string message)
            {
                if (LogLevelError == false)
                {
                    return;
                }

                Chat.PrintError(message);
            }
        }


        private void onCommand(string command, string args)
        {
            if (string.IsNullOrEmpty(args))
            {

                return;
            }

            // Initialize arguments from config
            GoConfig config = new();

            foreach (var word in args.Split(' '))
            {
                switch (word)
                {
                    case "--verbose":
                    case "-v":
                        config.LogLevelVerbose = true;
                        break;
                    case "--info":
                    case "-i":
                        config.LogLevelInfo = true;
                        break;
                    case "--error":
                    case "-e":
                        config.LogLevelError = true;
                        break;
                    case "--silent":
                    case "-s":
                        config.LogLevelVerbose = false;
                        config.LogLevelInfo = false;
                        config.LogLevelError = false;
                        break;
                    default:
                        if (config.Search == "")
                        {
                            config.Search = word.ToLower();
                        }
                        else
                        {
                            config.Search += " " + word.ToLower();
                        }
                        break;
                }
            }

            bestEffortMatch(config);
        }

        private unsafe void bestEffortMatch(GoConfig config)
        {
            var target = config.Search;

            // Store values for handling best-effort match
            double bestMatchSimilarity = 0f;
            var bestMatchName = "";
            var bestMatchMap = "";
            uint bestMatchId = 0;
            byte bestMatchSubId = 0;
            var time = DateTime.Now;

            // Iterate over aetherytes in the aetheryte list
            foreach (var entry in AetheryteList)
            {
                var aetheryte = entry.AetheryteData;
                var aetheryteData = aetheryte.GameData;

                // If we can't get aetheryte data fail fast
                if (aetheryteData == null)
                {
                    continue;
                }

                var aetheryteName = aetheryteData.PlaceName.Value?.Name.ToString();

                // If we don't have an aetheryte name fail fast
                if (aetheryteName == null)
                {
                    continue;
                }

                var territoryName = aetheryteData.Territory.Value?.PlaceName.Value?.Name.ToString();

                // If we don't have a territory name fail fast
                if (territoryName == null)
                {
                    continue;
                }

                var compareName = aetheryteName[..].ToLower();

                // If the entry is an apartment change the name
                // from "Estate Hall (private)" to "apartment"
                if (entry.IsAppartment)
                {
                    aetheryteName = "Apartment";
                    compareName = "Apartment";
                }

                // If the entry has a "The " prefix disregard it
                if (compareName.StartsWith("the "))
                {
                    compareName = compareName.Remove(0, 4);
                }

                // If the entry has a "Camp " prefix disregard it
                if (compareName.StartsWith("camp "))
                {
                    compareName = compareName.Remove(0, 5);
                }

                // Determine similarity
                var aetheryteSimilarity = similarity(target, compareName);

                // If aetheryte similarity exceeds our current best similarity replace
                // that match with new values
                if (aetheryteSimilarity > bestMatchSimilarity)
                {
                    bestMatchSimilarity = aetheryteSimilarity;
                    bestMatchName = aetheryteName;
                    bestMatchId = entry.AetheryteId;
                    bestMatchSubId = entry.SubIndex;
                    bestMatchMap = territoryName;
                }

                var territorySimilarity = similarity(target, territoryName.ToLower());

                // If territory similarity exceeds our current best similarity replace
                // that match with new values
                if (territorySimilarity > bestMatchSimilarity)
                {
                    bestMatchSimilarity = territorySimilarity;
                    bestMatchName = aetheryteName;
                    bestMatchId = entry.AetheryteId;
                    bestMatchSubId = entry.SubIndex;
                    bestMatchMap = territoryName;
                }

                if (bestMatchSimilarity == 1.0)
                {
                    break;
                }
            }

            var ishgardSimilarity = similarity(target, "ishgard");

            // If territory similarity exceeds our current best similarity replace
            // that match with new values
            if (ishgardSimilarity > bestMatchSimilarity)
            {
                bestMatchSimilarity = ishgardSimilarity;
                bestMatchName = "Foundation";
                bestMatchId = 70;
                bestMatchSubId = 0;
                bestMatchMap = "Foundation";
            }

            // If we got a partial match follow it
            if (bestMatchName != "")
            {
                _ = Telepo.Instance()->Teleport(bestMatchId, bestMatchSubId);
                config.LogInfo(bestMatchName == bestMatchMap
                    ? $"Go → {bestMatchName}"
                    : $"Go → {bestMatchName} in {bestMatchMap}");
                config.LogVerbose("Match found:"
                    + $"\nInput: {target}"
                    + $"\nAetheryte: {bestMatchName} - Zone: {bestMatchMap}"
                    + $"\nSimilarity: {bestMatchSimilarity}"
                    + $"\nAetheryteID: {bestMatchId} - Aetheryte SubID: {bestMatchSubId}"
                    + $"\nTime spent: {DateTime.Now.Subtract(time).TotalSeconds}s");
            }
            else
            {
                config.LogError($"Unable to find a matching aetheryte for {target}");
            }
        }



        // TODO: Implement similarity algorithm that accounts for missing
        // characters and transposed typos
        private double similarity(string input, string value)
        {
            if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(value))
            {
                return 0.0;
            }

            var inputWords = input.Split(" ");
            var valueWords = value.Split(" ");

            var matchResult = Enumerable.Repeat(-1.0, inputWords.Length).ToArray();

            for (var i = 0; i < inputWords.Length; i++)
            {
                var inputWord = inputWords[i];
                foreach (var valueWord in valueWords)
                {
                    var distance = valueWord.IndexOf(inputWord);
                    if (distance == -1)
                    {
                        continue;
                    }

                    var wordOffset = value.IndexOf(valueWord);

                    // Apply penalties; distance within a word weighs much heavier than distance between words
                    var wordWeight = value.Length * 10; // multiple word size by max penalty multiplier to ensure 1.0 boundings
                    var distancePenalty = distance * 10;
                    var wordOffsetPenalty = wordOffset * 1;

                    var sim = (double)(wordWeight - distancePenalty - wordOffsetPenalty) / wordWeight;
                    if (sim > matchResult[i])
                    {
                        matchResult[i] = sim;
                    }
                }
            }
            return matchResult.Sum() / inputWords.Length;
        }
    }
}
