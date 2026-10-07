// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Hoshino Kennji

// ============================================================================
// UBT 规则存根（stub）：本文件定义 UE 的 *.Build.cs 所依赖的最小对象模型，
// 使文档引擎能用 Roslyn 把 Build.cs 与本存根一起编译并反射实例化 ModuleRules。
//
// 兼容性哲学（数据收集优先）：
// 模拟 UBT 只是为了把 Build.cs 编译起来并反射出模块数据，不是复刻 UBT 行为
// ——宁可宽松，尽量少失败：
//   1) 目标规则对象走动态兜底：规则编译器把 Build.cs 中的 TargetRules /
//      ReadOnlyTargetRules 类型引用改写为 dynamic，一切 Target.* 成员访问在
//      运行时绑定；已知成员返回真实值，未知成员由 DynamicStubObject 返回
//      宽松默认值（false / "" / 可继续链式访问的动态存根）。
//   2) Build.cs 中不带 Target 前缀的裸名字（ModuleDirectory、RuntimeDependencies
//      等）无法动态兜底，按真实 UBT 定义补齐；定义以 UE 源码为准核对。
//   3) 个别仍编译失败的文件由规则编译器逐文件隔离，不拖累其他模块。
//
// 版本策略（测试驱动，不靠版本号推测）：本存根对某版本源码测试通过后，
// 若对更高版本也通过则共用；报错则按 compat 工具的诊断决定是否分叉扫描器子类。
// ============================================================================

using System.Dynamic;

namespace EpicGames
{
    /// <summary>占位类型：.NET 元数据不存在空命名空间，无类型则 using EpicGames.* 会 CS0246。</summary>
    internal static class NamespacePlaceholder
    {
    }
}

namespace EpicGames.Core
{
    /// <summary>占位类型：使 using EpicGames.Core 可绑定。</summary>
    internal static class NamespacePlaceholder
    {
    }
}

namespace UnrealBuildBase
{
    /// <summary>占位类型：使 using UnrealBuildBase 可绑定。</summary>
    internal static class NamespacePlaceholder
    {
    }
}

namespace UnrealBuildTool
{
    /// <summary>目标平台（仅列出 Build.cs 中常见的取值；缺失成员按测试反馈补齐）。</summary>
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

    /// <summary>目标配置。</summary>
    public enum UnrealTargetConfiguration
    {
        Unknown,
        Debug,
        DebugGame,
        Development,
        Shipping,
        Test,
    }

    /// <summary>目标类型。</summary>
    public enum TargetType
    {
        Game,
        Editor,
        Client,
        Server,
        Program,
    }

    /// <summary>模块类型。</summary>
    public enum ModuleType
    {
        CPlusPlus,
        External,
    }

    /// <summary>代码优化策略。</summary>
    public enum CodeOptimization
    {
        Never,
        InShippingBuildsOnly,
        Always,
    }

    /// <summary>Build 设置版本。</summary>
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

    /// <summary>引擎 include 顺序版本。</summary>
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

    /// <summary>IWYU 支持级别。</summary>
    public enum IWYUSupport
    {
        None,
        KeepEverything,
        KeepUsedOnly,
        Full,
    }

    /// <summary>编译警告级别（与真实 UBT 同名同值）。</summary>
    public enum WarningLevel
    {
        Default,
        Off,
        Warning,
        Error,
    }

    /// <summary>符号可见性（与真实 UBT 同名同值，含官方拼写 VisibileForDll）。</summary>
    public enum SymbolVisibility
    {
        Default,
        Public,
        Internal,
        Private,
        VisibileForDll,
    }

    /// <summary>平台分组（Build.cs 常用于 IsInPlatformGroup 判断；真实定义在 EpicGames.Build 程序集内）。</summary>
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

    /// <summary>暂存文件类型（与真实 UBT 同名同值）。</summary>
    public enum StagedFileType
    {
        UFS,
        NonUFS,
        DebugNonUFS,
    }

    /// <summary>Windows 目标体系结构。</summary>
    public enum WindowsArchitecture
    {
        x86,
        x64,
        ARM64,
        ARM64EC,
    }

    /// <summary>Windows 编译器。</summary>
    public enum WindowsCompiler
    {
        Default,
        VisualStudio2019,
        VisualStudio2022,
        Clang,
        IntelOneAPI,
    }

    /// <summary>Windows 静态分析模式。</summary>
    public enum WindowsStaticAnalyzer
    {
        None,
        PCSan,
        VCToolset,
        VisualCpp,
        PVSStudio,
    }

    /// <summary>链接方式。</summary>
    public enum TargetLinkType
    {
        Monolithic,
        Modular,
    }

    /// <summary>目标构建环境。</summary>
    public enum TargetBuildEnvironment
    {
        Shared,
        Exclusive,
    }

    /// <summary>平台类别（Utils.GetPlatformsInClass 参数）。</summary>
    public enum UnrealPlatformClass
    {
        All,
        Desktop,
        Editor,
    }

    /// <summary>PhysX 库模式。</summary>
    public enum PhysXLibraryMode
    {
        PhysX3,
        APEX,
        Disabled,
    }

    /// <summary>PhysXLibraryMode 后缀扩展（真实 UBT 中为扩展方法）。</summary>
    public static class PhysXLibraryModeExtensions
    {
        public static string AsSuffix(this PhysXLibraryMode mode) => "";
    }

    /// <summary>引擎级静态信息（RenderCore 等 Build.cs 引用 Unreal.EngineDirectory）。</summary>
    public static class Unreal
    {
        public static string EngineDirectory = "";
    }

    /// <summary>构建宿主平台信息（宽松版）。</summary>
    public class BuildHostPlatform
    {
        public static BuildHostPlatform Current { get; } = new();

        public UnrealTargetPlatform Platform => UnrealTargetPlatform.Win64;

        public TargetType Type => TargetType.Editor;
    }

    /// <summary>UBT 日志（宽松版：只吞参数）。</summary>
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

    /// <summary>UBT 规则编译异常：Build.cs 中可能 throw，仅需可构造。</summary>
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

    /// <summary>UBT 工具方法（Build.cs 常用静态帮助器的宽松版：直接透传输入）。</summary>
    public static class Utils
    {
        public static string MakePathRelativeTo(string path, string directory) => path;

        public static string MakePathRelativeTo(string path, string directory, bool exceptions) => path;

        public static string RunLocalProcessAndReturnStdOut(params object[] args) => "Xcode 13.2.1\nBuild version 13C100\n";

        public static string[] GetPlatformsInClass(UnrealPlatformClass platformClass) => [];
    }

    /// <summary>平台支持标注：Build.cs 顶层 [SupportedPlatforms(...)]（真实用法为字符串或枚举值，宽松收下）。</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
    public class SupportedPlatformsAttribute : Attribute
    {
        public SupportedPlatformsAttribute(params object[] platforms)
        {
        }
    }

    /// <summary>
    /// 动态兜底对象：一切成员访问 / 方法调用 / 索引 / 转换都成功并返回宽松默认值。
    /// 数据收集视角下，未知成员的真实值并不重要，重要的是不让 Build.cs 执行中断。
    /// 静态隐式转换运算符兜住 DLR 不走 TryConvert 的转换路径（bool / string / int / 比较 / 取反）。
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

        /// <summary>按期望类型给宽松默认值；未知引用类型给可继续链式访问的动态存根。</summary>
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
    /// 平台值包装：dynamic 派发找不到扩展方法，IsInGroup 必须是实例方法；
    /// 隐式转换保证 == / != / ToString 等与真实枚举互通。
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

    /// <summary>Windows 平台设置存根：高频成员给真实类型值，未知成员仍走动态兜底。</summary>
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
    /// 目标规则存根：Build.cs 通过构造参数只读访问目标信息。
    /// 已知常用成员给出真实值；其余一切成员经 DynamicStubObject 兜底。
    /// TargetType 结构与真实 UBT 一致：命名空间级枚举 + 本类内嵌静态别名类
    /// （Build.cs 同时存在 TargetRules.TargetType.Server 与裸名 TargetType.Editor 两种用法）。
    /// </summary>
    public class TargetRules : DynamicStubObject
    {
        /// <summary>静态别名类：使 TargetRules.TargetType.Server 这类限定用法可绑定。</summary>
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

        /// <summary>引擎目录。</summary>
        public string EngineDir { get; set; } = "";

        /// <summary>项目文件路径（无项目时为 null）。</summary>
        public string? ProjectFile { get; set; }

        public bool bBuildEditor { get; set; } = true;

        public int VersionBuildNumber { get; set; }

        /// <summary>引擎相对路径（真实 UBT 语义为相对路径；此处给引擎目录，字符串场景可用）。</summary>
        public string RelativeEnginePath { get; set; } = "";

        /// <summary>第三方源码目录。</summary>
        public string UEThirdPartySourceDirectory { get; set; } = "";

        /// <summary>第三方二进制目录。</summary>
        public string UEThirdPartyBinariesDirectory { get; set; } = "";

        /// <summary>目标体系结构。</summary>
        public string Architecture { get; set; } = "";

        public bool bDebugBuildsActuallyUseDebugCRT { get; set; }

        /// <summary>Windows 平台专属设置（高频成员真实类型，未知成员动态兜底）。</summary>
        public WindowsPlatformRules WindowsPlatform { get; } = new();

        public bool IsInPlatformGroup(UnrealPlatformGroup group) => false;

        /// <summary>返回 dynamic：调用方可能继续链式调用真实 UBT 的扩展方法（如 AsSuffix）。</summary>
        public dynamic GetPhysXLibraryMode(params object[] args) => new DynamicStubObject();
    }

    /// <summary>只读目标规则（5.x 的 Build.cs 构造签名使用它）。</summary>
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

        /// <summary>编辑器目标语义下默认 true（CookedEditor / ApplicationCore 等模块的守卫条件）。</summary>
        public bool bCompileAgainstEngine { get; set; } = true;

        public bool bCompileAgainstApplicationCore { get; set; } = true;
    }

    /// <summary>UnrealTargetPlatform 分组扩展（真实 UBT 中为扩展方法，Build.cs 直接调用）。</summary>
    public static class UnrealTargetPlatformExtensions
    {
        public static bool IsInGroup(this UnrealTargetPlatform platform, UnrealPlatformGroup group) => false;
    }

    /// <summary>
    /// 模块规则存根：*.Build.cs 的基类。
    /// <para>
    /// PCHUsageMode / PackageOverrideType / PrecompileTargetsType 与真实 UBT 一致嵌套在本类内
    /// （Build.cs 存在 ModuleRules.PCHUsageMode 这样的限定用法）。
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

        /// <summary>Apple 框架依赖（宽松版：只收条目）。</summary>
        public class Framework
        {
            public Framework(params object[] args)
            {
            }
        }

        /// <summary>打包附加资源（宽松版：只收条目）。</summary>
        public class BundleResource
        {
            public BundleResource(params object[] args)
            {
            }

            public BundleResource(string path, bool bShouldLog = false)
            {
            }
        }

        /// <summary>类型库引用（宽松版：只收条目）。</summary>
        public class TypeLibrary
        {
            public TypeLibrary(params object[] args)
            {
            }
        }

        /// <summary>运行时依赖列表（宽松版：只收条目，不做任何处理）。</summary>
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

        /// <summary>Receipt 属性列表（宽松版：只收条目）。</summary>
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
            // 参数用 object：Build.cs 的 base(Target) 在构造初始化器里不允许动态派发（CS1975），
            // object 可以静态绑定 dynamic 实参；运行时真实对象就是 ReadOnlyTargetRules。
            Target = target as ReadOnlyTargetRules ?? new ReadOnlyTargetRules();
            Name = Target.GetType().Name; // 占位：真实模块名由扫描器按目录名覆盖
        }

        /// <summary>目标规则（只读）。</summary>
        public ReadOnlyTargetRules Target { get; }

        /// <summary>模块名（多数 Build.cs 不显式赋值，扫描器以 .Build.cs 文件名推断）。</summary>
        public string Name { get; set; }

        /// <summary>模块目录（规则编译器按 .Build.cs 所在目录填入）。</summary>
        public string ModuleDirectory { get; set; } = "";

        /// <summary>模块类型（引擎模块 / 第三方 External）。</summary>
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

        // ---- include 路径 ----
        public List<string> PublicIncludePaths { get; } = [];
        public List<string> PrivateIncludePaths { get; } = [];
        public List<string> PublicSystemIncludePaths { get; } = [];
        public List<string> PrivateSystemIncludePaths { get; } = [];

        // ---- 模块依赖 ----
        public List<string> PublicDependencyModuleNames { get; } = [];
        public List<string> PrivateDependencyModuleNames { get; } = [];
        public List<string> PublicIncludePathModuleNames { get; } = [];
        public List<string> PrivateIncludePathModuleNames { get; } = [];
        public List<string> DynamicallyLoadedModuleNames { get; } = [];
        public List<string> CircularlyReferencedDependentModules { get; } = [];

        // ---- 定义与第三方库 ----
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

        // ---- 常见开关（按测试反馈持续补齐）----
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

        /// <summary>引擎目录（裸名字使用的宽松别名，取自目标规则）。</summary>
        public string EngineDirectory => Target.EngineDir;

        /// <summary>AutoSDK 目录（裸名字使用的宽松别名）。</summary>
        public string AutoSdkDirectory => "";

        public bool IsVcPackageSupported { get; set; }

        public void AddVcPackage(params object[] args)
        {
        }

        /// <summary>真实 UBT 签名：把引擎第三方模块加入私有静态依赖（宽松版：只收参数）。</summary>
        public void AddEngineThirdPartyPrivateStaticDependencies(ReadOnlyTargetRules target, params string[] moduleNames)
        {
        }

        /// <summary>无 Target 变体（个别 Build.cs 直接传模块名）。</summary>
        public void AddEngineThirdPartyPrivateStaticDependencies(params string[] moduleNames)
        {
        }

        /// <summary>真实 UBT 签名：物理支持模块的公共设置（宽松版：空操作）。</summary>
        public void SetupModulePhysicsSupport(ReadOnlyTargetRules target)
        {
        }

        public void SetupModulePhysicsSupport()
        {
        }
    }
}
