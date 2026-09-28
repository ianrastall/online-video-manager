param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
$buildRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot) '.build'))
$target = [IO.Path]::GetFullPath($Path)
if (-not $target.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove a directory outside .build: $target"
}
if (Test-Path -LiteralPath $target) {
    # Do not traverse junctions or symlinks, including any ancestor of the target.
    for ($parent = Get-Item -LiteralPath $target; $parent; $parent = $parent.Parent) {
        if ($parent.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Refusing to clean a linked directory: $($parent.FullName)"
        }
    }
    if (Get-ChildItem -LiteralPath $target -Recurse -Force -Attributes ReparsePoint) {
        throw "Refusing to clean a directory containing links: $target"
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}
