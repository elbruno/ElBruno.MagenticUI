try {
    $dllPath = "C:\Users\brunocapuano\.nuget\packages\microsoft.extensions.ai.abstractions\10.8.3\lib\net9.0\Microsoft.Extensions.AI.Abstractions.dll"
    if (-not (Test-Path $dllPath)) {
        $dllPath = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\microsoft.extensions.ai.abstractions\*" -Recurse -Filter "Microsoft.Extensions.AI.Abstractions.dll" | Select-Object -ExpandProperty FullName -First 1
    }
    # Load dependencies first to avoid issues
    [Reflection.Assembly]::LoadWithPartialName("System.Runtime") | Out-Null
    
    $asm = [Reflection.Assembly]::LoadFrom($dllPath)

    function Get-TypeSummary($typeName) {
        process {
            Write-Host "`n--- $typeName ---"
            $type = $asm.GetType($typeName)
            if ($null -eq $type) { Write-Host "Type not found."; return }
            Write-Host "Full Name: $($type.FullName)"
            
            if ($type.IsInterface) {
                Write-Host "Public Members:"
                $type.GetMembers([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | ForEach-Object {
                    Write-Host "  $($_.ToString())"
                }
            } else {
                if ($typeName -eq "Microsoft.Extensions.AI.ChatMessage" -or $typeName -eq "Microsoft.Extensions.AI.FunctionCallContent") {
                    Write-Host "Constructors:"
                    $type.GetConstructors() | ForEach-Object { Write-Host "  $($_.ToString())" }
                } elseif ($typeName -eq "Microsoft.Extensions.AI.ChatResponse") {
                    Write-Host "Constructors:"
                    $type.GetConstructors() | ForEach-Object { Write-Host "  $($_.ToString())" }
                    Write-Host "Public Properties:"
                    $type.GetProperties() | ForEach-Object { Write-Host "  $($_.ToString())" }
                }
            }
        }
    }

    "Microsoft.Extensions.AI.IChatClient", "Microsoft.Extensions.AI.ChatResponse", "Microsoft.Extensions.AI.ChatMessage", "Microsoft.Extensions.AI.FunctionCallContent" | Get-TypeSummary
} catch {
    Write-Error $_
}
