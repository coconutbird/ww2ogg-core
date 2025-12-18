# ww2ogg-netcore

A .NET port of ww2ogg - converts Wwise RIFF/RIFX Vorbis audio files (.wem) to standard Ogg Vorbis format.

## Projects

- **Ww2Ogg** - Command-line application
- **Ww2Ogg.Core** - Library for programmatic usage

## Installation

### Library (NuGet)

```bash
dotnet add package Ww2Ogg.Core
```

### Command Line

```bash
dotnet build -c Release
```

## Library Usage

```csharp
using Ww2Ogg.Core;

// Convert a .wem file to .ogg using the default codebook
using var input = File.OpenRead("input.wem");
using var output = File.Create("output.ogg");

var converter = new WwiseRiffVorbis(input, CodebookLibrary.Default);
converter.GenerateOgg(output);

// Optionally validate the output
output.Position = 0;
VorbisValidator.Validate(output);
```

### Codebook Options

```csharp
// Default packed codebooks
var codebook = CodebookLibrary.Default;

// aoTuV 6.03 codebooks (for files encoded with aoTuV)
var codebook = CodebookLibrary.AoTuV;

// Empty codebook (for files with inline codebooks)
var codebook = new CodebookLibrary();

// Load from file
var codebook = new CodebookLibrary("path/to/packed_codebooks.bin");
```

### Force Packet Format

```csharp
// Auto-detect (default)
var converter = new WwiseRiffVorbis(input, codebook);

// Force specific format
var converter = new WwiseRiffVorbis(
    input, 
    codebook, 
    forcePacketFormat: ForcePacketFormat.ModPackets
);
```

## Command Line Usage

```
ww2ogg <input.wem> [options]

Options:
  -o, --output <file>     Output file (default: input with .ogg extension)
  --pcb <file>            Use external packed codebooks file
  --inline-codebooks      Use inline codebooks from the wem file
  --full-setup            Output full setup header
  --aoTuV                 Use aoTuV 6.03 codebooks
  --mod-packets           Force modified packet format
  --no-mod-packets        Force standard packet format
```

### Examples

```bash
# Basic conversion (auto-detects codebook)
ww2ogg music.wem

# Specify output file
ww2ogg music.wem -o soundtrack.ogg

# Use aoTuV codebooks
ww2ogg music.wem --aoTuV

# Use inline codebooks
ww2ogg music.wem --inline-codebooks
```

## Public API Reference

### Classes

- **`WwiseRiffVorbis`** - Main converter class
- **`CodebookLibrary`** - Manages Vorbis codebooks
- **`VorbisValidator`** - Validates Ogg Vorbis output

### Enums

- **`ForcePacketFormat`** - `NoForce`, `ForceModPackets`, `ForceNoModPackets`

### Exceptions

- **`WemException`** - Base exception for all wem-related errors
- **`ParseException`** - Error parsing wem file structure
- **`CodebookException`** - Codebook-related errors
- **`FileOpenException`** - File not found or cannot be opened
- **`InvalidCodebookIdException`** - Invalid codebook ID in file
- **`SizeMismatchException`** - Size validation failed

## Building

```bash
dotnet build
dotnet test
```

## License

This project is licensed under the BSD-3-Clause License - see the [LICENSE](LICENSE) file for details.

## Credits

This is a .NET port of [ww2ogg](https://github.com/hcs64/ww2ogg) by Adam Gashlin (hcs), which includes code derived from the Xiph.org Foundation's reference Vorbis implementation.

