// Golden dump tool. Reads an array of assembler inputs from cases.json, runs the shared pure
// PromptAssembler, and writes the rendered system/user prompts to expected.json. Paths default to
// golden/cases.json and golden/expected.json relative to the current directory (npm runs it from the
// prompt-lab folder). Regenerate after changing PromptAssembler; the Node check then verifies the JS
// mirror matches.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using PawnDiary;

namespace PawnDiary.PromptDump
{
    // One golden case: a full assembler input plus an id so the Node side can match outputs.
    internal sealed class GoldenCase : PromptAssemblerInput
    {
        public string id { get; set; }
    }

    internal sealed class GoldenOutput
    {
        public string id { get; set; }
        public string systemPrompt { get; set; }
        public string userPrompt { get; set; }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            string casesPath = args.Length > 0 ? args[0] : Path.Combine("golden", "cases.json");
            string outPath = args.Length > 1 ? args[1] : Path.Combine("golden", "expected.json");

            if (!File.Exists(casesPath))
            {
                Console.Error.WriteLine("cases file not found: " + Path.GetFullPath(casesPath));
                return 1;
            }

            // IncludeFields: the assembler types use public fields, not properties.
            JsonSerializerOptions readOptions = new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true,
            };

            List<GoldenCase> cases = JsonSerializer.Deserialize<List<GoldenCase>>(
                File.ReadAllText(casesPath), readOptions) ?? new List<GoldenCase>();

            List<GoldenOutput> outputs = new List<GoldenOutput>();
            foreach (GoldenCase testCase in cases)
            {
                PromptAssemblerResult result = PromptAssembler.Render(testCase);
                outputs.Add(new GoldenOutput
                {
                    id = testCase.id,
                    systemPrompt = result.systemPrompt,
                    userPrompt = result.userPrompt,
                });
            }

            JsonSerializerOptions writeOptions = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, JsonSerializer.Serialize(outputs, writeOptions));
            Console.WriteLine("Wrote " + outputs.Count + " cases to " + Path.GetFullPath(outPath));
            return 0;
        }
    }
}
