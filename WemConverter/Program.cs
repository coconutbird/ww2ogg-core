namespace WemConverter;

public static class Program
{
    private const string DefaultCodebook = "WemConverter.Codebooks.packed_codebooks.bin";
    private const string AoTuVCodebook = "WemConverter.Codebooks.packed_codebooks_aoTuV_603.bin";

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();

            return 1;
        }

        string? inputFile = null;
        string? outputFile = null;
        string? codebookPath = null;
        bool useAoTuV = false;
        bool inlineCodebooks = false;
        bool fullSetup = false;
        var forcePacketFormat = ForcePacketFormat.NoForce;

        // Parse arguments
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-o":
                case "--output":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: -o requires an argument");

                        return 1;
                    }

                    outputFile = args[++i];

                    break;

                case "--pcb":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Error: --pcb requires an argument");

                        return 1;
                    }

                    codebookPath = args[++i];

                    break;

                case "--aotuv":
                    useAoTuV = true;

                    break;

                case "--inline-codebooks":
                    inlineCodebooks = true;

                    break;

                case "--full-setup":
                    fullSetup = true;

                    break;

                case "--mod-packets":
                    forcePacketFormat = ForcePacketFormat.ForceModPackets;

                    break;

                case "--no-mod-packets":
                    forcePacketFormat = ForcePacketFormat.ForceNoModPackets;

                    break;

                case "-h":
                case "--help":
                    PrintUsage();

                    return 0;

                default:
                    if (args[i].StartsWith("-"))
                    {
                        Console.Error.WriteLine($"Error: Unknown option {args[i]}");

                        return 1;
                    }

                    inputFile = args[i];

                    break;
            }
        }

        if (inputFile == null)
        {
            Console.Error.WriteLine("Error: No input file specified");

            return 1;
        }

        if (!File.Exists(inputFile))
        {
            Console.Error.WriteLine($"Error: Input file not found: {inputFile}");

            return 1;
        }

        // Default output file
        outputFile ??= Path.ChangeExtension(inputFile, ".ogg");

        try
        {
            // Load codebook library
            CodebookLibrary codebooks;

            if (codebookPath != null)
            {
                codebooks = new CodebookLibrary(codebookPath);
            }
            else if (inlineCodebooks)
            {
                codebooks = new CodebookLibrary();
            }
            else
            {
                string resourceName = useAoTuV ? AoTuVCodebook : DefaultCodebook;
                codebooks = CodebookLibrary.FromEmbeddedResource(resourceName);
            }

            // Convert
            using var inputStream = File.OpenRead(inputFile);
            using var outputStream = File.Create(outputFile);

            var converter = new WwiseRiffVorbis(
                inputStream,
                codebooks,
                inlineCodebooks,
                fullSetup,
                forcePacketFormat);

            converter.GenerateOgg(outputStream);

            Console.WriteLine($"Converted: {inputFile} -> {outputFile}");

            return 0;
        }
        catch (WemException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Unexpected error: {ex.Message}");

            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("WemConverter - Convert Wwise .wem files to Ogg Vorbis");
        Console.WriteLine();
        Console.WriteLine("Usage: WemConverter <input.wem> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <file>    Output file (default: input with .ogg extension)");
        Console.WriteLine("  --pcb <file>           Path to packed codebooks file");
        Console.WriteLine("  --aotuv                Use aoTuV 6.03 codebooks (built-in)");
        Console.WriteLine("  --inline-codebooks     Codebooks are inline in the file");
        Console.WriteLine("  --full-setup           Full setup header present");
        Console.WriteLine("  --mod-packets          Force modified Vorbis packets");
        Console.WriteLine("  --no-mod-packets       Force standard Vorbis packets");
        Console.WriteLine("  -h, --help             Show this help");
    }
}