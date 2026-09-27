# Dumps Assembly-CSharp members for types/fields the mod touches.
# Uses MetadataLoadContext so Unity native deps are not required at runtime.

param(
    [Parameter(Mandatory = $true)]
    [string] $AssembliesDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [Parameter(Mandatory = $false)]
    [string] $Label = "assemblies"
)

$ErrorActionPreference = 'Stop'

$assemblyPath = Join-Path $AssembliesDirectory 'Assembly-CSharp.dll'
if (-not (Test-Path -LiteralPath $assemblyPath)) {
    throw "Assembly-CSharp.dll not found under $AssembliesDirectory"
}

$resolverSources = @(Get-ChildItem -LiteralPath $AssembliesDirectory -Filter '*.dll' | ForEach-Object { $_.FullName })
$ummDir = Join-Path $AssembliesDirectory 'UnityModManager'
if (Test-Path -LiteralPath $ummDir) {
    $resolverSources += @(Get-ChildItem -LiteralPath $ummDir -Filter '*.dll' | ForEach-Object { $_.FullName })
}

$projectDir = Join-Path ([System.IO.Path]::GetTempPath()) ("et-api-dump-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $projectDir | Out-Null

try {
    $csproj = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
    Set-Content -LiteralPath (Join-Path $projectDir 'Dumper.csproj') -Value $csproj -Encoding UTF8

    $program = @'
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;
using System.Text;

static string FormatType(Type type)
{
    if (type.IsByRef) return FormatType(type.GetElementType()!) + "&";
    if (type.IsPointer) return FormatType(type.GetElementType()!) + "*";
    if (type.IsArray) return FormatType(type.GetElementType()!) + "[]";
    if (!type.IsGenericType) return type.FullName ?? type.Name;
    string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
    int tick = name.IndexOf('`');
    if (tick >= 0) name = name[..tick];
    return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FormatType)) + ">";
}

static void DumpType(Type? type, StringBuilder sb)
{
    if (type is null)
    {
        sb.AppendLine("  <type not found>");
        return;
    }

    sb.AppendLine($"  FullName: {type.FullName}");
    sb.AppendLine($"  BaseType: {type.BaseType?.FullName ?? "<none>"}");
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    foreach (FieldInfo field in type.GetFields(flags).OrderBy(f => f.Name, StringComparer.Ordinal))
    {
        string vis = field.IsPublic ? "public" : field.IsFamily ? "protected" : field.IsAssembly ? "internal" : "private";
        sb.AppendLine($"  FIELD {vis} {FormatType(field.FieldType)} {field.Name}");
    }

    foreach (PropertyInfo prop in type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
    {
        sb.AppendLine($"  PROP {FormatType(prop.PropertyType)} {prop.Name}");
    }

    foreach (MethodInfo method in type.GetMethods(flags).OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.GetParameters().Length))
    {
        if (method.IsSpecialName) continue;
        string vis = method.IsPublic ? "public" : method.IsFamily ? "protected" : method.IsAssembly ? "internal" : "private";
        string args = string.Join(", ", method.GetParameters().Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
        sb.AppendLine($"  METHOD {vis} {FormatType(method.ReturnType)} {method.Name}({args})");
    }
}

string assembliesDir = args[0];
string outputPath = args[1];
string label = args.Length > 2 ? args[2] : "assemblies";
string assemblyPath = Path.Combine(assembliesDir, "Assembly-CSharp.dll");

var dllPaths = Directory.GetFiles(assembliesDir, "*.dll").ToList();
string umm = Path.Combine(assembliesDir, "UnityModManager");
if (Directory.Exists(umm)) dllPaths.AddRange(Directory.GetFiles(umm, "*.dll"));

var resolver = new PathAssemblyResolver(dllPaths);
using var mlc = new MetadataLoadContext(resolver);
Assembly asm = mlc.LoadFromAssemblyPath(assemblyPath);

string[] typeNames =
[
    "scnEditor",
    "scrDecoration",
    "scrDecorationManager",
    "scrParallax",
    "DecorationPivot",
    "scrVfxPlus",
    "ADOFAI.Editor.Components.DraggableNumberInputField",
    "ADOFAI.LevelEditor.Controls.PropertyControl_Text",
    "ADOFAI.LevelEditor.Controls.PropertyControl_Vector2",
    "ADOFAI.LevelEditor.Controls.PropertyControl_Slider",
    "ADOFAI.Editor.Preferences.Models.EditorPreferencesEntry",
    "ADOFAI.Editor.Preferences.Controls.EditorPreferencesControl",
    "PropertyInfo",
    "ADOFAI.PropertyInfo",
    "LoadResult",
    "ADOFAI.LoadResult",
    "DecPlacementType",
    "ADOBase",
    "scrLevelMaker",
    "scnGame",
    "Persistence",
];

string[] requiredFields =
[
    "scnEditor::decorationPositionsAtDragStart",
    "scnEditor::addXDragCache",
    "scnEditor::addYDragCache",
    "scnEditor::errorImageResult",
    "scnEditor::isUnauthorizedAccess",
    "scnEditor::selectedDecorations",
    "scnEditor::draggingGizmo",
    "scnEditor::decPivot",
    "scnEditor::levelEventsPanel",
    "scnEditor::camera",
    "scnEditor::floors",
    "scnEditor::isLoading",
    "DraggableNumberInputField::_startValue",
    "DraggableNumberInputField::_startPos",
    "DraggableNumberInputField::_isDragging",
    "DraggableNumberInputField::_down",
    "DraggableNumberInputField::field",
    "DraggableNumberInputField::arrows",
    "scrVfxPlus::videoBG",
    "scrVfxPlus::hasPlayed",
    "scrVfxPlus::vidOffset",
    "scrDecoration::parallax",
    "scrDecoration::pivotPosVec",
    "scrDecoration::pivotOffsetVec",
    "scrDecoration::placementType",
    "scrDecoration::sourceLevelEvent",
    "scrDecoration::forceLock",
    "DecorationPivot::gizmoTransform",
];

string[] requiredMethods =
[
    "scnEditor::DragDecorations",
    "scnEditor::DragDecorationsStart",
    "scnEditor::UpdateImageLoadResult",
    "scnEditor::SelectionDecorationIsSingle",
    "scnEditor::SelectionDecorationIsEmpty",
    "scnEditor::GetDecorationDragDelta",
    "scnEditor::UpdateDecorationObject",
    "DecorationPivot::UpdatePivotCrossImage",
    "scrDecoration::UpdateScreenClamp",
    "scrDecoration::SetPosition",
    "scrParallax::SetTrans",
    "scrVfxPlus::Reset",
    "scrVfxPlus::Update",
    "DraggableNumberInputField::OnPointerDown",
    "DraggableNumberInputField::OnPointerUp",
    "DraggableNumberInputField::SetArrowsVisible",
    "PropertyControl_Text::Setup",
    "PropertyControl_Vector2::Setup",
    "EditorPreferencesEntry::NotifyChange",
];

var sb = new StringBuilder();
sb.AppendLine($"# API dump label={label}");
sb.AppendLine($"# Assembly: {assemblyPath}");
sb.AppendLine($"# AssemblyName: {asm.GetName().FullName}");
sb.AppendLine();

Dictionary<string, Type> bySimple = new(StringComparer.Ordinal);
Dictionary<string, Type> byFull = new(StringComparer.Ordinal);
foreach (Type t in asm.GetTypes())
{
    if (!string.IsNullOrEmpty(t.FullName)) byFull[t.FullName] = t;
    if (!bySimple.ContainsKey(t.Name)) bySimple[t.Name] = t;
}

Type? Resolve(string name)
{
    if (byFull.TryGetValue(name, out Type? full)) return full;
    string shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;
    return bySimple.TryGetValue(shortName, out Type? simple) ? simple : null;
}

foreach (string typeName in typeNames)
{
    sb.AppendLine($"TYPE {typeName}");
    DumpType(Resolve(typeName), sb);
    sb.AppendLine();
}

sb.AppendLine("# Required field checks");
foreach (string item in requiredFields)
{
    string[] parts = item.Split(new[] { "::" }, StringSplitOptions.None);
    Type? type = Resolve(parts[0]);
    FieldInfo? field = type?.GetField(parts[1], BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    sb.AppendLine(field is null ? $"MISSING_FIELD {item}" : $"OK_FIELD {item} -> {FormatType(field.FieldType)}");
}

sb.AppendLine();
sb.AppendLine("# Required method checks");
foreach (string item in requiredMethods)
{
    string[] parts = item.Split(new[] { "::" }, StringSplitOptions.None);
    Type? type = Resolve(parts[0]);
    MethodInfo[] methods = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Where(m => m.Name == parts[1]).ToArray() ?? Array.Empty<MethodInfo>();
    if (methods.Length == 0)
    {
        sb.AppendLine($"MISSING_METHOD {item}");
    }
    else
    {
        foreach (MethodInfo method in methods)
        {
            string args = string.Join(", ", method.GetParameters().Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
            sb.AppendLine($"OK_METHOD {item}({args})");
        }
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
File.WriteAllText(outputPath, sb.ToString());
Console.WriteLine($"Wrote {outputPath}");
'@
    Set-Content -LiteralPath (Join-Path $projectDir 'Program.cs') -Value $program -Encoding UTF8

    Push-Location $projectDir
    try {
        & dotnet run -c Release -- "$AssembliesDirectory" "$OutputPath" "$Label"
        if ($LASTEXITCODE -ne 0) {
            throw "API dump failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item -LiteralPath $projectDir -Recurse -Force -ErrorAction SilentlyContinue
}
