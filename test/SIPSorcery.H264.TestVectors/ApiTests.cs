//-----------------------------------------------------------------------------
// Filename: ApiTests.cs
//
// Description: E8 acceptance: scripted calls of the encoder API between the
// frames (ForceIntraFrame, EncodeParameterSets, SetOption and GetOption for
// each option, Uninitialize, Initialize again and bad inputs) must return
// what the C-only reference build returns and produce the same bytes and
// values (refharness `h264ref api`, reference/api.tsv). The stream of each
// configuration must then decode with the ported decoder without errors.
//
// The script language is the harness's (refharness/src/encoder.cpp,
// kApiConfigs). The statistics GetOption reports leave out
// fAverageFrameSpeedInMs, the encoder's own timing.
//
// Set H264_DUMP_DIR to have a failing row write its stream there, to compare
// with `h264ref api-dump <name> <out>`.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E8).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SIPSorcery.H264.Encoder;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

/// <summary>One configuration of reference/api.tsv, with its op rows.</summary>
public sealed record ApiReference(string Name, string Content, string Settings, string Script)
{
    public int InitReturnCode { get; set; } = int.MinValue;
    public string Sha1 { get; set; }
    public int Size { get; set; }

    /// <summary>Each operation as the harness prints it: index, op, return code, then what it produced.</summary>
    public List<string> Ops { get; } = new();

    public override string ToString() => Name;
}

public static class ApiReferences
{
    public const string TableFile = "api.tsv";

    private static readonly Lazy<IReadOnlyList<ApiReference>> _rows = new(Load);

    public static IReadOnlyList<ApiReference> Rows => _rows.Value;

    public static IEnumerable<object[]> Names => Rows.Select(r => new object[] { r.Name });

    private static IReadOnlyList<ApiReference> Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, TableFile);
        var rows = new List<ApiReference>();
        var byName = new Dictionary<string, ApiReference>();
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] f = line.Split('\t');
            switch (f[0])
            {
                case "config":
                    var r = new ApiReference(f[1], f[2], f[3], f[4]);
                    rows.Add(r);
                    byName.Add(r.Name, r);
                    break;
                case "init":
                    byName[f[1]].InitReturnCode = int.Parse(f[2], CultureInfo.InvariantCulture);
                    break;
                case "op":
                    byName[f[1]].Ops.Add(string.Join('\t', f.Skip(2)));
                    break;
                case "stream":
                    ApiReference c = byName[f[1]];
                    c.Sha1 = f[2];
                    c.Size = int.Parse(f[3], CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new InvalidDataException($"{TableFile}: unknown row {f[0]}.");
            }
        }
        return rows;
    }
}

public class ApiTests
{
    private const string DumpEnvironmentVariable = "H264_DUMP_DIR";

    private readonly ITestOutputHelper _output;

    public ApiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TableCoversTheApi()
    {
        Assert.True(ApiReferences.Rows.Count >= 50);
        var ops = ApiReferences.Rows.SelectMany(r => r.Ops).Select(o => o.Split('\t')[1]).ToList();
        foreach (string key in new[] { "idr", "br", "maxbr", "fps", "rc", "skip", "pad", "ltr", "ltrperiod", "ltrreq", "ltrfb",
                                       "ssei", "prefix", "strategy", "profile", "level", "nref", "delivery", "complexity",
                                       "getstats", "statint", "lossless", "vary", "fmt", "interpred", "base", "ext", "unknown" })
        {
            Assert.Contains(ops, o => o.StartsWith($"S:{key}=", StringComparison.Ordinal));
        }
        foreach (string key in new[] { "idr", "br", "maxbr", "fps", "ext", "base", "stats", "statint", "complexity", "fmt", "interpred" })
        {
            Assert.Contains(ops, o => o == $"G:{key}" || o.StartsWith($"G:{key}=", StringComparison.Ordinal));
        }
        Assert.True(ops.Count(o => o == "I") >= 30);
        Assert.All(ApiReferences.Rows, r => Assert.Equal(ApiScript.Expand(r.Script).Count, r.Ops.Count));
    }

    [Theory]
    [MemberData(nameof(ApiReferences.Names), MemberType = typeof(ApiReferences))]
    public unsafe void ApiMatchesReference(string name)
    {
        ApiReference expected = ApiReferences.Rows.Single(r => r.Name == name);
        var ts = new TimestampSchedule();
        string settings = ts.Split(expected.Settings);
        ParamSetEncoder.ParseSettings(settings, out SEncParamExt ext0, out SEncParamBase base0, out bool isBase);

        using var enc = new EncoderDriver();
        Assert.Equal(expected.InitReturnCode, isBase ? enc.Initialize(base0) : enc.InitializeExt(ext0));

        var chunks = new List<byte[]>();
        var stream = new List<byte>();
        List<string> ops = ApiScript.Expand(expected.Script);
        int frame = 0;
        for (int i = 0; i < ops.Count; i++)
        {
            string op = ops[i];
            int rv;
            string detail = "-";
            EncodedFrame got = null;
            if (op[0] == 'F')
            {
                int pw = enc.PicWidth, ph = enc.PicHeight, fmt = (int)EVideoFormatType.videoFormatI420;
                if (op.StartsWith("F:fmt=", StringComparison.Ordinal))
                {
                    fmt = int.Parse(op[6..], CultureInfo.InvariantCulture);
                }
                else if (op.Length > 2)
                {
                    string[] wh = op[2..].Split('x');
                    pw = int.Parse(wh[0], CultureInfo.InvariantCulture);
                    ph = int.Parse(wh[1], CultureInfo.InvariantCulture);
                }
                got = enc.EncodeFrame(StreamContent.Fill(pw, ph, expected.Content, frame), pw, ph, fmt, ts.At(frame));
                frame++;
                rv = got.ReturnCode;
            }
            else if (op == "P")
            {
                got = enc.EncodeParameterSets();
                rv = got.ReturnCode;
            }
            else if (op == "I" || op == "i")
            {
                rv = enc.ForceIntraFrame(op == "I");
            }
            else if (op == "U")
            {
                rv = enc.Encoder->Uninitialize();
            }
            else if (op == "R")
            {
                rv = isBase ? enc.Initialize(base0) : enc.InitializeExt(ext0);
            }
            else if (op.StartsWith("S:", StringComparison.Ordinal))
            {
                int eq = op.IndexOf('=');
                string k = op[2..eq], v = op[(eq + 1)..];
                rv = ApiScript.Set(enc, k, v);
                if (rv == 0 && (k == "base" || k == "ext"))
                {
                    // The frames follow the new size.
                    SEncParamExt e = default;
                    enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, ref e);
                    enc.SetPictureSize(e.iPicWidth, e.iPicHeight);
                }
            }
            else if (op.StartsWith("G:", StringComparison.Ordinal))
            {
                rv = ApiScript.Get(enc, op[2..], out detail);
            }
            else
            {
                throw new InvalidDataException($"{name}: unknown op {op}.");
            }

            if (got != null)
            {
                stream.AddRange(got.Bytes);
                if (got.Bytes.Length != 0)
                {
                    chunks.Add(got.Bytes);
                }
                detail = string.Join('\t', got.FrameType, got.TemporalIds, got.Layers, got.Sha1,
                                     got.Bytes.Length.ToString(CultureInfo.InvariantCulture));
            }
            string row = string.Join('\t', i.ToString(CultureInfo.InvariantCulture), op, rv.ToString(CultureInfo.InvariantCulture),
                                     detail);
            _output.WriteLine(row);
            if (row != expected.Ops[i])
            {
                Dump(name, stream);
                foreach (string l in enc.Log)
                {
                    _output.WriteLine(l);
                }
            }
            Assert.Equal(expected.Ops[i], row);
        }

        byte[] all = stream.ToArray();
        Assert.Equal(expected.Sha1, all.Length == 0 ? "-" : EncoderDriver.Sha1(all));
        Assert.Equal(expected.Size, all.Length);

        // The stream decodes without errors, but for the two rows where upstream's own
        // decoder (h264ref hash on the api-dump stream) also reports one.
        int decoded = 0;
        DecodeResult d = DecoderHash.DecodeChunks(chunks, (y, u, v) => decoded++);
        _output.WriteLine($"{decoded} pictures decoded from {chunks.Count} chunks, {d.ErrorChunks} with errors " +
                          $"(first state 0x{d.FirstErrorState:x})");
        Assert.Equal(KnownDecodeErrors.GetValueOrDefault(name), d.ErrorChunks);

        Assert.Equal(0u, enc.Release());
    }

    /// <summary>
    /// The rows whose streams upstream's decoder does not decode cleanly either, with its
    /// error count (h264ref hash): parameter-sets, where EncodeParameterSets between the
    /// frames gives the next frames new parameter set ids, and set-number-ref, where
    /// ENCODER_OPTION_NUMBER_REF raises the reference count without a new SPS.
    /// </summary>
    private static readonly Dictionary<string, int> KnownDecodeErrors = new()
    {
        ["parameter-sets"] = 1,
        ["set-number-ref"] = 1,
    };

    private static void Dump(string name, List<byte> stream)
    {
        string dir = Environment.GetEnvironmentVariable(DumpEnvironmentVariable);
        if (!string.IsNullOrEmpty(dir))
        {
            File.WriteAllBytes(Path.Combine(dir, $"{name}.api.264"), stream.ToArray());
        }
    }
}

/// <summary>The harness's api script (refharness/src/encoder.cpp ApiOps, ApiSet and ApiGet).</summary>
internal static class ApiScript
{
    /// <summary>The script's operations, with op*n expanded.</summary>
    public static List<string> Expand(string script)
    {
        var ops = new List<string>();
        foreach (string token in script.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string op = token;
            int n = 1;
            int star = op.LastIndexOf('*');
            if (star >= 0)
            {
                n = int.Parse(op[(star + 1)..], CultureInfo.InvariantCulture);
                op = op[..star];
            }
            for (int i = 0; i < n; i++)
            {
                ops.Add(op);
            }
        }
        return ops;
    }

    /// <summary>"a:b:c" as up to five integers, missing ones 0, as strtoll reads them.</summary>
    private static long[] Ints(string v)
    {
        var n = new long[5];
        string[] parts = v.Split(':');
        for (int i = 0; i < parts.Length && i < n.Length; i++)
        {
            n[i] = StrToLL(parts[i]);
        }
        return n;
    }

    /// <summary>strtoll (s, NULL, 10): the leading integer, 0 when there is none.</summary>
    private static long StrToLL(string s)
    {
        int i = 0;
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+'))
        {
            i++;
        }
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            i++;
        }
        return long.TryParse(s.AsSpan(start, i - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long r) ? r : 0;
    }

    private static string F(float x) => ((double)x).ToString("G9", CultureInfo.InvariantCulture);

    /// <summary>Applies "k:v+k:v" to the parameters, as ParamSetEncoder.ApplySetting.</summary>
    private static void Apply(ref SEncParamExt e, ref SEncParamBase b, bool isBase, string v)
    {
        foreach (string item in v.Split('+'))
        {
            int colon = item.IndexOf(':');
            if (!ParamSetEncoder.ApplySetting(ref e, ref b, isBase, item[..colon], item[(colon + 1)..]))
            {
                throw new InvalidDataException($"Unknown setting {item}.");
            }
        }
    }

    /// <summary>SetOption: key=value (ApiSet).</summary>
    public static unsafe int Set(EncoderDriver enc, string key, string v)
    {
        long[] n = Ints(v);
        int i = (int)n[0];
        switch (key)
        {
            case "idr": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_IDR_INTERVAL, i);
            case "rc": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_RC_MODE, i);
            case "pad": return enc.SetOption(ENCODER_OPTION.ENCODER_PADDING_PADDING, i);
            case "strategy": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_SPS_PPS_ID_STRATEGY, i);
            case "nref": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_NUMBER_REF, i);
            case "complexity": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_COMPLEXITY, i);
            case "statint": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_STATISTICS_LOG_INTERVAL, i);
            case "vary": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_BITS_VARY_PERCENTAGE, i);
            case "fmt": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_DATAFORMAT, i);
            case "interpred": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_INTER_SPATIAL_PRED, i);
            case "unknown": return enc.SetOption((ENCODER_OPTION)99, i);
            case "ltrperiod": return enc.SetOption(ENCODER_OPTION.ENCODER_LTR_MARKING_PERIOD, (uint)n[0]);
            case "fps": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_FRAME_RATE, (float)double.Parse(v, CultureInfo.InvariantCulture));
            case "skip": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_RC_FRAME_SKIP, i != 0);
            case "ssei": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_ENABLE_SSEI, i != 0);
            case "prefix": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_ENABLE_PREFIX_NAL_ADDING, i != 0);
            case "lossless": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_IS_LOSSLESS_LINK, i != 0);
            case "br":
            case "maxbr":
                return enc.SetOption(key == "br" ? ENCODER_OPTION.ENCODER_OPTION_BITRATE : ENCODER_OPTION.ENCODER_OPTION_MAX_BITRATE,
                                     new SBitrateInfo { iLayer = (LAYER_NUM)n[0], iBitrate = (int)n[1] });
            case "ltr":
                return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_LTR,
                                     new SLTRConfig { bEnableLongTermReference = n[0] != 0, iLTRRefNum = (int)n[1] });
            case "ltrreq":
                return enc.SetOption(ENCODER_OPTION.ENCODER_LTR_RECOVERY_REQUEST, new SLTRRecoverRequest
                {
                    uiFeedbackType = (uint)n[0],
                    uiIDRPicId = (uint)n[1],
                    iLastCorrectFrameNum = (int)n[2],
                    iCurrentFrameNum = (int)n[3],
                    iLayerId = (int)n[4],
                });
            case "ltrfb":
                return enc.SetOption(ENCODER_OPTION.ENCODER_LTR_MARKING_FEEDBACK, new SLTRMarkingFeedback
                {
                    uiFeedbackType = (uint)n[0],
                    uiIDRPicId = (uint)n[1],
                    iLTRFrameNum = (int)n[2],
                    iLayerId = (int)n[3],
                });
            case "profile":
                return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_PROFILE,
                                     new SProfileInfo { iLayer = (int)n[0], uiProfileIdc = (EProfileIdc)n[1] });
            case "level":
                return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_LEVEL,
                                     new SLevelInfo { iLayer = (int)n[0], uiLevelIdc = (ELevelIdc)n[1] });
            case "delivery":
                return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_DELIVERY_STATUS, new SDeliveryStatus
                {
                    bDeliveryFlag = n[0] != 0,
                    iDropFrameType = (int)n[1],
                    iDropFrameSize = (int)n[2],
                });
            case "getstats": return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_GET_STATISTICS, default(SEncoderStatistics));
            case "base":
            {
                // The current parameters (zeroed when GetOption fails), with the changes.
                SEncParamBase b = default;
                SEncParamExt e = default;
                enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, ref b);
                Apply(ref e, ref b, true, v);
                return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, b);
            }
            case "ext":
            {
                SEncParamBase b = default;
                SEncParamExt e = default;
                enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, ref e);
                Apply(ref e, ref b, false, v);
                return enc.SetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, e);
            }
            default:
                throw new InvalidDataException($"Unknown option {key}.");
        }
    }

    /// <summary>GetOption: key (ApiGet). The value is "-" when GetOption fails.</summary>
    public static int Get(EncoderDriver enc, string key, out string value)
    {
        value = "-";
        int rv;
        int eq = key.IndexOf('=');
        string k = eq < 0 ? key : key[..eq];
        switch (k)
        {
            case "idr":
            case "statint":
            case "complexity":
            case "fmt":
            case "interpred":
            case "unknown":
            case "ltr":
            {
                int i = 0x5a5a5a5a;
                ENCODER_OPTION o = k switch
                {
                    "idr" => ENCODER_OPTION.ENCODER_OPTION_IDR_INTERVAL,
                    "statint" => ENCODER_OPTION.ENCODER_OPTION_STATISTICS_LOG_INTERVAL,
                    "complexity" => ENCODER_OPTION.ENCODER_OPTION_COMPLEXITY,
                    "fmt" => ENCODER_OPTION.ENCODER_OPTION_DATAFORMAT,
                    "interpred" => ENCODER_OPTION.ENCODER_OPTION_INTER_SPATIAL_PRED,
                    "ltr" => ENCODER_OPTION.ENCODER_OPTION_LTR,
                    _ => (ENCODER_OPTION)99,
                };
                rv = enc.GetOption(o, ref i);
                if (rv == 0)
                {
                    value = i.ToString(CultureInfo.InvariantCulture);
                }
                return rv;
            }
            case "fps":
            {
                float f = -1;
                rv = enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_FRAME_RATE, ref f);
                if (rv == 0)
                {
                    value = F(f);
                }
                return rv;
            }
            case "br":
            case "maxbr":
            {
                var info = new SBitrateInfo { iLayer = (LAYER_NUM)StrToLL(key[(eq + 1)..]), iBitrate = -1 };
                rv = enc.GetOption(k == "br" ? ENCODER_OPTION.ENCODER_OPTION_BITRATE : ENCODER_OPTION.ENCODER_OPTION_MAX_BITRATE, ref info);
                if (rv == 0)
                {
                    value = info.iBitrate.ToString(CultureInfo.InvariantCulture);
                }
                return rv;
            }
            case "ext":
            {
                SEncParamExt e = default;
                rv = enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, ref e);
                if (rv == 0)
                {
                    value = ParamSetEncoder.DumpParamExt(e);
                }
                return rv;
            }
            case "base":
            {
                SEncParamBase b = default;
                rv = enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_BASE, ref b);
                if (rv == 0)
                {
                    value = $"iUsageType={(int)b.iUsageType};iPicWidth={b.iPicWidth};iPicHeight={b.iPicHeight};" +
                            $"iTargetBitrate={b.iTargetBitrate};iRCMode={(int)b.iRCMode};fMaxFrameRate={F(b.fMaxFrameRate)}";
                }
                return rv;
            }
            case "stats":
            {
                // fAverageFrameSpeedInMs is the encoder's own timing, so it is left out.
                SEncoderStatistics s = default;
                rv = enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_GET_STATISTICS, ref s);
                if (rv == 0)
                {
                    value = string.Create(CultureInfo.InvariantCulture,
                        $"uiWidth={s.uiWidth};uiHeight={s.uiHeight};fAverageFrameRate={F(s.fAverageFrameRate)};" +
                        $"fLatestFrameRate={F(s.fLatestFrameRate)};uiBitRate={s.uiBitRate};uiAverageFrameQP={s.uiAverageFrameQP};" +
                        $"uiInputFrameCount={s.uiInputFrameCount};uiSkippedFrameCount={s.uiSkippedFrameCount};" +
                        $"uiResolutionChangeTimes={s.uiResolutionChangeTimes};uiIDRReqNum={s.uiIDRReqNum};" +
                        $"uiIDRSentNum={s.uiIDRSentNum};uiLTRSentNum={s.uiLTRSentNum}");
                }
                return rv;
            }
            default:
                throw new InvalidDataException($"Unknown option {key}.");
        }
    }
}
