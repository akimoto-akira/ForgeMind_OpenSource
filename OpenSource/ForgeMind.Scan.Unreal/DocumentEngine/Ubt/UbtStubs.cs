// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// ============================================================================
// UBT rule stubs: this file defines the minimal object model that UE *.Build.cs
// files depend on, so the document engine can compile Build.cs together with
// these stubs via Roslyn and reflectively instantiate ModuleRules.
//
// Compatibility philosophy (data collection first):
// We simulate UBT only to compile Build.cs and reflect out module data — not to
// replicate UBT behavior. Leniency over strictness, fail as little as possible:
//   1) Target rule objects use dynamic fallback: the rule compiler rewrites
//      TargetRules / ReadOnlyTargetRules type references in Build.cs to dynamic;
//      every Target.* member access binds at runtime; known members return real
//      values, unknown members return lenient defaults via DynamicStubObject
//      (false / "" / a chainable dynamic stub).
//   2) Bare names in Build.cs without the Target prefix (ModuleDirectory,
//      RuntimeDependencies, etc.) cannot be dynamically backed; we supply them
//      per the real UBT definitions, verified against UE source.
//   3) Files that still fail to compile are isolated per-file by the rule
//      compiler and do not drag down other modules.
//
// Versioning strategy (test-driven, no version-guessing): once this stub passes
// tests against a version's source, it is shared with higher versions that also
// pass; on failure, the compat tool's diagnostic decides whether to fork the
// scanner subclass.
// ============================================================================

using System.Dynamic;

namespace EpicGames
{
    /// <summary>Placeholder type: .NET metadata has no "empty namespace"; without a type, using EpicGames.* triggers CS0246.</summary>
    internal static class NamespacePlaceholder
    {
    }
}

namespace EpicGames.Core
{
    /// <summary>Placeholder type: allows using EpicGames.Core to bind.</summary>
    internal static class NamespacePlaceholder
    {
    }
}

namespace UnrealBuildBase
{
    /// <summary>Placeholder type: allows using UnrealBuildBase to bind.</summary>
    internal static class NamespacePlaceholder
    {
    }
}

namespace UnrealBuildTool
{
    /// <summary>Target platform (only lists values commonly seen in Build.cs; missing members are added per test feedback).</summary>
    public enum UnrealTargetPlatform
    {
        Win32,
        Win64,
        Linux,
        LinuxArm64,
        Mac,
        IOS,
        TVOS,
        Android,
        HoloLens,
    }

    /// <summary>Target configuration.</summary>
    public enum UnrealTargetConfiguration
    {
        Unknown,
        Debug,
        DebugGame,
        Development,
        Shipping,
        Test,
    }

    /// <summary>Target type.</summary>
    public enum TargetType
    {
        Game,
        Editor,
        Client,
        Server,
        Program,
    }

    /// <summary>Module type.</summary>
    public enum ModuleType
    {
        CPlusPlus,
        External,
    }

    /// <summary>Code optimization strategy.</summary>
    public enum CodeOptimization
    {
        Never,
        InShippingBuildsOnly,
        Always,
    }

    /// <summary>Build settings version.</summary>
    public enum BuildSettingsVersion
    {
        Latest,
        Oldest,
        V1,
        V2,
        V3,
        V4,
        V5,
    }

    /// <summary>Engine include order version.</summary>
    public enum EngineIncludeOrderVersion
    {
        Latest,
        Oldest,
        Unreal5_0,
        Unreal5_1,
        Unreal5_2,
        Unreal5_3,
        Unreal5_4,
        Unreal5_5,
        Unreal5_6,
        Unreal5_7,
        Unreal5_8,
    }

    /// <summary>IWYU support level.</summary>
    public enum IWYUSupport
    {
        None,
        KeepEverything,
        KeepUsedOnly,
        Full,
    }

    /// <summary>Compile warning level (same name and value as real UBT).</summary>
    public enum WarningLevel
    {
        Default,
        Off,
        Warning,
        Error,
    }

    /// <summary>Symbol visibility (same name and value as real UBT, including the official misspelling VisibileForDll).</summary>
    public enum SymbolVisibility
    {
        Default,
        Public,
        Internal,
        Private,
        VisibileForDll,
    }

    /// <summary>Platform group (Build.cs commonly uses it in IsInPlatformGroup checks; the real definition lives in the EpicGames.Build assembly).</summary>
    public enum UnrealPlatformGroup
    {
        Android,
        Apple,
        Desktop,
        Google,
        IOS,
        Linux,
        Mac,
        Microsoft,
        TVOS,
        Unix,
        Windows,
    }

    /// <summary>Staged file type (same name and value as real UBT).</summary>
    public enum StagedFileType
    {
        UFS,
        NonUFS,
        DebugNonUFS,
    }

    /// <summary>Windows target architecture.</summary>
    public enum WindowsArchitecture
    {
        x86,
        x64,
        ARM64,
        ARM64EC,
    }

    /// <summary>Windows compiler.</summary>
    public enum WindowsCompiler
    {
        Default,
        VisualStudio2019,
        VisualStudio2022,
        Clang,
        IntelOneAPI,
    }

    /// <summary>Windows static analysis mode.</summary>
    public enum WindowsStaticAnalyzer
    {
        None,
        PCSan,
        VCToolset,
        VisualCpp,
        PVSStudio,
    }

    /// <summary>Link type.</summary>
    public enum TargetLinkType
    {
        Monolithic,
        Modular,
    }

    /// <summary>Target build environment.</summary>
    public enum TargetBuildEnvironment
    {
        Shared,
        Exclusive,
    }

    /// <summary>Platform class (parameter of Utils.GetPlatformsInClass).</summary>
    public enum UnrealPlatformClass
    {
        All,
        Desktop,
        Editor,
    }

    /// <summary>PhysX library mode.</summary>
    public enum PhysXLibraryMode
    {
        PhysX3,
        APEX,
        Disabled,
    }

    /// <summary>PhysXLibraryMode suffix extension (an extension method in real UBT).</summary>
    public static class PhysXLibraryModeExtensions
    {
        public static string AsSuffix(this PhysXLibraryMode mode) => "";
    }

    /// <summary>Engine-level static info (Build.cs files such as RenderCore reference Unreal.EngineDirectory).</summary>
    public static class Unreal
    {
        public static string EngineDirectory = "";
    }

    /// <summary>Build host platform info (lenient version).</summary>
    public class BuildHostPlatform
    {
        public static BuildHostPlatform Current { get; } = new();

        public UnrealTargetPlatform Platform => UnrealTargetPlatform.Win64;

        public TargetType Type => TargetType.Editor;
    }

    /// <summary>UBT log (lenient version: swallows arguments).</summary>
    public static class Log
    {
        public static void TraceInformation(string format, params object?[] args)
        {
        }

        public static void TraceInformationOnce(string format, params object?[] args)
        {
        }

        public static void TraceWarning(string format, params object?[] args)
        {
        }

        public static void TraceWarningOnce(string format, params object?[] args)
        {
        }

        public static void TraceError(string format, params object?[] args)
        {
        }

        public static void TraceLog(string format, params object?[] args)
        {
        }

        public static void WriteLineToLog(string format, params object?[] args)
        {
        }
    }

    /// <summary>UBT rule compile exception: Build.cs may throw this; only needs to be constructible.</summary>
    public class BuildException : Exception
    {
        public BuildException()
        {
        }

        public BuildException(string message)
            : base(message)
        {
        }

        public BuildException(string format, params object?[] args)
            : base(string.Format(format, args))
        {
        }

        public BuildException(Exception innerException, string message)
            : base(message, innerException)
        {
        }

        public BuildException(Exception innerException, string format, params object?[] args)
            : base(string.Format(format, args), innerException)
        {
        }
    }

    /// <summary>UBT utility methods (lenient version of commonly-used static helpers in Build.cs: passes input through).</summary>
    public static class Utils
    {
        public static string MakePathRelativeTo(string path, string directory) => path;

        public static string MakePathRelativeTo(string path, string directory, bool exceptions) => path;

        public static string RunLocalProcessAndReturnStdOut(params object[] args) => "Xcode 13.2.1\nBuild version 13C100\n";

        public static string[] GetPlatformsInClass(UnrealPlatformClass platformClass) => [];
    }

    /// <summary>Platform support attribute: top-level [SupportedPlatforms(...)] on Build.cs (real usage takes strings or enum values; lenient version accepts both).</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class SupportedPlatformsAttribute : Attribute
    {
        public SupportedPlatformsAttribute(params object[] platforms)
        {
        }
    }

    /// <summary>
    /// Dynamic fallback object: every member access / method call / index / conversion
    /// succeeds and returns a lenient default. From the data-collection perspective,
    /// the true value of an unknown member does not matter; the key is to not let
    /// Build.cs execution abort. Static implicit conversion operators cover the DLR
    /// paths that bypass TryConvert (bool / string / int / comparison / negation).
    /// </summary>
    public class DynamicStubObject : DynamicObject, System.Collections.IEnumerable
    {
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            Array.Empty<object>().GetEnumerator();

        public static implicit operator bool(DynamicStubObject stub) => false;

        public static implicit operator string(DynamicStubObject stub) => "";

        public static implicit operator int(DynamicStubObject stub) => 0;

        public static bool operator !(DynamicStubObject stub) => true;

        public static bool operator ==(DynamicStubObject? stub, object? other) => false;

        public static bool operator !=(DynamicStubObject? stub, object? other) => true;

        public override bool Equals(object? obj) => ReferenceEquals(this, obj);

        public override int GetHashCode() => 0;

        public override bool TryGetMember(GetMemberBinder binder, out object? result)
        {
            result = StubValue(binder.ReturnType);
            return true;
        }

        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
        {
            result = StubValue(binder.ReturnType);
            return true;
        }

        public override bool TrySetMember(SetMemberBinder binder, object? value) => true;

        public override bool TryGetIndex(GetIndexBinder binder, object?[] indexes, out object? result)
        {
            result = new DynamicStubObject();
            return true;
        }

        public override bool TrySetIndex(SetIndexBinder binder, object?[] indexes, object? value) => true;

        public override bool TryConvert(ConvertBinder binder, out object? result)
        {
            result = StubValue(binder.Type);
            return true;
        }

        /// <summary>Returns a lenient default for the expected type; unknown reference types get a chainable dynamic stub.</summary>
        internal static object? StubValue(Type expected)
        {
            if (expected == typeof(void))
                return null;
            if (expected == typeof(bool))
                return false;
            if (expected == typeof(string))
                return "";
            if (expected.IsEnum)
                return Enum.ToObject(expected, 0);
            if (expected.IsArray)
                return Array.CreateInstance(expected.GetElementType()!, 0);
            if (expected.IsValueType)
                return Activator.CreateInstance(expected);
            return new DynamicStubObject();
        }
    }

    /// <summary>
    /// Platform value wrapper: dynamic dispatch cannot find extension methods, so
    /// IsInGroup must be an instance method. Implicit conversions ensure == / != /
    /// ToString interoperate with the real enum.
    /// </summary>
    public class UnrealPlatformValue : DynamicStubObject
    {
        private readonly UnrealTargetPlatform _value;

        public UnrealPlatformValue(UnrealTargetPlatform value)
        {
            _value = value;
        }

        public bool IsInGroup(UnrealPlatformGroup group) => false;

        public override string ToString() => _value.ToString();

        public static implicit operator UnrealTargetPlatform(UnrealPlatformValue value) => value._value;

        public static bool operator ==(UnrealPlatformValue? a, UnrealTargetPlatform b) => a is not null && a._value == b;

        public static bool operator !=(UnrealPlatformValue? a, UnrealTargetPlatform b) => a is null || a._value != b;

        public override bool Equals(object? obj) => obj is UnrealPlatformValue other && other._value == _value;

        public override int GetHashCode() => (int)_value;
    }

    /// <summary>Windows platform settings stub: high-frequency members get real-typed values; unknown members still fall back to dynamic.</summary>
    public class WindowsPlatformRules : DynamicStubObject
    {
        public string WindowsSdkVersion { get; set; } = "10.0.0.0";

        public int TargetWindowsVersion { get; set; } = 0x0603;

        public WindowsCompiler Compiler { get; set; } = WindowsCompiler.VisualStudio2019;

        public WindowsStaticAnalyzer StaticAnalyzer { get; set; } = WindowsStaticAnalyzer.None;

        public WindowsArchitecture Architecture { get; set; } = WindowsArchitecture.x64;

        public string GetVisualStudioCompilerVersionName() => "14.0";
    }

    /// <summary>
    /// Target rules stub: Build.cs accesses target info read-only via the constructor parameter.
    /// Known commonly-used members return real values; everything else falls back through DynamicStubObject.
    /// TargetType mirrors real UBT structure: namespace-level enum + nested static alias class
    /// (Build.cs uses both TargetRules.TargetType.Server and the bare name TargetType.Editor).
    /// </summary>
    public class TargetRules : DynamicStubObject
    {
        /// <summary>Static alias class: enables qualified usage like TargetRules.TargetType.Server to bind.</summary>
        public static class TargetType
        {
            public const global::UnrealBuildTool.TargetType Game = global::UnrealBuildTool.TargetType.Game;
            public const global::UnrealBuildTool.TargetType Editor = global::UnrealBuildTool.TargetType.Editor;
            public const global::UnrealBuildTool.TargetType Client = global::UnrealBuildTool.TargetType.Client;
            public const global::UnrealBuildTool.TargetType Server = global::UnrealBuildTool.TargetType.Server;
            public const global::UnrealBuildTool.TargetType Program = global::UnrealBuildTool.TargetType.Program;
        }

        public UnrealPlatformValue Platform { get; set; } = new(UnrealTargetPlatform.Win64);

        public UnrealTargetConfiguration Configuration { get; set; } = UnrealTargetConfiguration.Development;

        public global::UnrealBuildTool.TargetType Type { get; set; } = global::UnrealBuildTool.TargetType.Editor;

        /// <summary>Engine directory.</summary>
        public string EngineDir { get; set; } = "";

        /// <summary>Project file path (null when no project).</summary>
        public string? ProjectFile { get; set; }

        public bool bBuildEditor { get; set; } = true;

        public int VersionBuildNumber { get; set; }

        /// <summary>Engine-relative path (real UBT semantics are relative; here we give the engine dir for string scenarios).</summary>
        public string RelativeEnginePath { get; set; } = "";

        /// <summary>Third-party source directory.</summary>
        public string UEThirdPartySourceDirectory { get; set; } = "";

        /// <summary>Third-party binaries directory.</summary>
        public string UEThirdPartyBinariesDirectory { get; set; } = "";

        /// <summary>Target architecture.</summary>
        public string Architecture { get; set; } = "";

        public bool bDebugBuildsActuallyUseDebugCRT { get; set; }

        /// <summary>Windows platform-specific settings (high-frequency members real-typed, unknown members dynamically backed).</summary>
        public WindowsPlatformRules WindowsPlatform { get; } = new();

        public bool IsInPlatformGroup(UnrealPlatformGroup group) => false;

        /// <summary>Returns dynamic: the caller may chain further calls to real UBT extension methods (e.g. AsSuffix).</summary>
        public dynamic GetPhysXLibraryMode(params object[] args) => new DynamicStubObject();
    }

    /// <summary>Read-only target rules (the Build.cs constructor signature in 5.x uses this).</summary>
    public class ReadOnlyTargetRules : TargetRules
    {
        public ReadOnlyTargetRules()
        {
        }

        public ReadOnlyTargetRules(TargetRules rules)
        {
            Platform = rules.Platform;
            Configuration = rules.Configuration;
            Type = rules.Type;
            EngineDir = rules.EngineDir;
            ProjectFile = rules.ProjectFile;
            bBuildEditor = rules.bBuildEditor;
            VersionBuildNumber = rules.VersionBuildNumber;
            RelativeEnginePath = rules.RelativeEnginePath;
            UEThirdPartySourceDirectory = rules.UEThirdPartySourceDirectory;
            UEThirdPartyBinariesDirectory = rules.UEThirdPartyBinariesDirectory;
        }

        public bool bUseShippingPhysXLibraries { get; set; }

        public bool bUseCheckedPhysXLibraries { get; set; }

        /// <summary>Defaults to true under editor target semantics (guard condition for CookedEditor / ApplicationCore, etc.).</summary>
        public bool bCompileAgainstEngine { get; set; } = true;

        public bool bCompileAgainstApplicationCore { get; set; } = true;
    }

    /// <summary>UnrealTargetPlatform group extensions (extension methods in real UBT; Build.cs calls them directly).</summary>
    public static class UnrealTargetPlatformExtensions
    {
        public static bool IsInGroup(this UnrealTargetPlatform platform, UnrealPlatformGroup group) => false;
    }

    /// <summary>
    /// Module rules stub: the base class of *.Build.cs.
    /// <para>
    /// PCHUsageMode / PackageOverrideType / PrecompileTargetsType mirror real UBT by being nested in this class
    /// (Build.cs uses qualified forms like ModuleRules.PCHUsageMode).
    /// </para>
    /// </summary>
    public abstract class ModuleRules
    {
        public enum PCHUsageMode
        {
            Default,
            NoPCHs,
            NoSharedPCHs,
            UseSharedPCHs,
            UseExplicitOrSharedPCHs,
        }

        public enum PackageOverrideType
        {
            None,
            EditorOnly,
            EngineDeveloper,
            GameDeveloper,
            EngineUncookedOnly,
            GameUncookedOnly,
        }

        public enum PrecompileTargetsType
        {
            None,
            Default,
            Game,
            Editor,
            Any,
        }

        public enum SymbolVisibility
        {
            Default,
            Public,
            Internal,
            Private,
            VisibileForDll,
        }

        /// <summary>Apple framework dependency (lenient version: only collects entries).</summary>
        public class Framework
        {
            public Framework(params object[] args)
            {
            }
        }

        /// <summary>Packaged additional resource (lenient version: only collects entries).</summary>
        public class BundleResource
        {
            public BundleResource(params object[] args)
            {
            }

            public BundleResource(string path, bool bShouldLog = false)
            {
            }
        }

        /// <summary>Type library reference (lenient version: only collects entries).</summary>
        public class TypeLibrary
        {
            public TypeLibrary(params object[] args)
            {
            }
        }

        /// <summary>Runtime dependency list (lenient version: only collects entries, does no processing).</summary>
        public class RuntimeDependencyList
        {
            public void Add(string path)
            {
            }

            public void Add(string path, StagedFileType type)
            {
            }

            public void Add(string path, string sourcePath)
            {
            }

            public void Add(string path, string sourcePath, StagedFileType type)
            {
            }
        }

        /// <summary>Receipt property list (lenient version: only collects entries).</summary>
        public class ReceiptPropertyList
        {
            public void Add(string key)
            {
            }

            public void Add(string key, string value)
            {
            }
        }

        protected ModuleRules(object target)
        {
            // Parameter is object: Build.cs base(Target) in a constructor initializer forbids dynamic dispatch (CS1975);
            // object statically binds to a dynamic argument; the runtime real object is ReadOnlyTargetRules.
            Target = target as ReadOnlyTargetRules ?? new ReadOnlyTargetRules();
            Name = Target.GetType().Name; // Placeholder: the real module name is overwritten by the scanner per directory name
        }

        /// <summary>Target rules (read-only).</summary>
        public ReadOnlyTargetRules Target { get; }

        /// <summary>Module name (most Build.cs files do not assign it explicitly; the scanner infers it from the .Build.cs file name).</summary>
        public string Name { get; set; }

        /// <summary>Module directory (the rule compiler fills it from the .Build.cs directory).</summary>
        public string ModuleDirectory { get; set; } = "";

        /// <summary>Module type (engine module / third-party External).</summary>
        public ModuleType Type { get; set; } = ModuleType.CPlusPlus;

        public PCHUsageMode PCHUsage { get; set; } = PCHUsageMode.UseExplicitOrSharedPCHs;

        public BuildSettingsVersion DefaultBuildSettings { get; set; } = BuildSettingsVersion.Latest;

        public EngineIncludeOrderVersion IncludeOrderVersion { get; set; } = EngineIncludeOrderVersion.Latest;

        public IWYUSupport IWYUSupport { get; set; } = IWYUSupport.KeepUsedOnly;

        public CodeOptimization OptimizeCode { get; set; } = CodeOptimization.InShippingBuildsOnly;

        public SymbolVisibility ModuleSymbolVisibility { get; set; } = SymbolVisibility.Default;

        public PackageOverrideType OverridePackageType { get; set; } = PackageOverrideType.None;

        public PrecompileTargetsType PrecompileForTargets { get; set; } = PrecompileTargetsType.Default;

        public WarningLevel UnsafeTypeCastWarningLevel { get; set; } = WarningLevel.Default;

        public bool bRequiresImplementModule { get; set; }

        public bool? IsRedistributableOverride { get; set; }

        public string BinariesSubFolder { get; set; } = "";

        public string ShortName { get; set; } = "";

        // ---- include paths ----
        public List<string> PublicIncludePaths { get; } = [];
        public List<string> PrivateIncludePaths { get; } = [];
        public List<string> PublicSystemIncludePaths { get; } = [];
        public List<string> PrivateSystemIncludePaths { get; } = [];

        // ---- module dependencies ----
        public List<string> PublicDependencyModuleNames { get; } = [];
        public List<string> PrivateDependencyModuleNames { get; } = [];
        public List<string> PublicIncludePathModuleNames { get; } = [];
        public List<string> PrivateIncludePathModuleNames { get; } = [];
        public List<string> DynamicallyLoadedModuleNames { get; } = [];
        public List<string> CircularlyReferencedDependentModules { get; } = [];

        // ---- defines and third-party libs ----
        public List<string> PublicDefinitions { get; } = [];
        public List<string> PrivateDefinitions { get; } = [];
        public List<string> PublicAdditionalLibraries { get; } = [];
        public List<object> PublicFrameworks { get; } = [];
        public List<string> PublicSystemLibraries { get; } = [];
        public List<object> PublicAdditionalFrameworks { get; } = [];
        public List<string> PublicSystemLibraryPaths { get; } = [];
        public List<string> PublicRuntimeLibraryPaths { get; } = [];
        public List<string> PrivateRuntimeLibraryPaths { get; } = [];
        public List<string> PublicWeakFrameworks { get; } = [];
        public List<string> PublicDelayLoadDLLs { get; } = [];
        public RuntimeDependencyList RuntimeDependencies { get; } = new();
        public ReceiptPropertyList AdditionalPropertiesForReceipt { get; } = new();
        public List<BundleResource> AdditionalBundleResources { get; } = [];
        public List<TypeLibrary> TypeLibraries { get; } = [];

        // ---- common switches (continuously added per test feedback) ----
        public bool bUsePrecompiled { get; set; }
        public bool bTreatAsEngineModule { get; set; }
        public bool bEnableUndefinedIdentifierWarnings { get; set; }
        public bool bLegacyPublicIncludePaths { get; set; }
        public bool bUseUnity { get; set; } = true;
        public bool bBuildLocallyWithRBX { get; set; }
        public WarningLevel ShadowVariableWarningLevel { get; set; } = WarningLevel.Default;
        public bool bDisableAutoRTFMInstrument { get; set; }
        public bool bEnableExceptions { get; set; }
        public bool bLegalToDistributeObjectCode { get; set; }
        public int MinFilesUsingPrecompiledHeaderOverride { get; set; }
        public string? PrivatePCHHeaderFile { get; set; }
        public string? SharedPCHHeaderFile { get; set; }
        public List<string> AllowedRestrictedFolders { get; } = [];

        /// <summary>Engine directory (lenient alias used by bare names, sourced from the target rules).</summary>
        public string EngineDirectory => Target.EngineDir;

        /// <summary>AutoSDK directory (lenient alias used by bare names).</summary>
        public string AutoSdkDirectory => "";

        public bool IsVcPackageSupported { get; set; }

        public void AddVcPackage(params object[] args)
        {
        }

        /// <summary>Real UBT signature: adds engine third-party modules to private static dependencies (lenient version: only collects arguments).</summary>
        public void AddEngineThirdPartyPrivateStaticDependencies(ReadOnlyTargetRules target, params string[] moduleNames)
        {
        }

        /// <summary>Target-less variant (some Build.cs pass module names directly).</summary>
        public void AddEngineThirdPartyPrivateStaticDependencies(params string[] moduleNames)
        {
        }

        /// <summary>Real UBT signature: public settings for physics-support modules (lenient version: no-op).</summary>
        public void SetupModulePhysicsSupport(ReadOnlyTargetRules target)
        {
        }

        public void SetupModulePhysicsSupport()
        {
        }
    }
}
