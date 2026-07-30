
$dllPath = "C:\Users\brunocapuano\.nuget\packages\microsoft.extensions.ai.abstractions\10.8.3\lib\net9.0\Microsoft.Extensions.AI.Abstractions.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
$types = "Microsoft.Extensions.AI.IChatClient", "Microsoft.Extensions.AI.ChatResponse", "Microsoft.Extensions.AI.ChatMessage", "Microsoft.Extensions.AI.FunctionCallContent"

foreach ($typeName in $types) {
    Write-Output "--- $typeName ---"
    $type = $asm.GetType($typeName)
    if ($null -eq $type) { continue }
    
    if ($typeName -eq "Microsoft.Extensions.AI.IChatClient") {
        $type.GetMethods() | Where-Object { $_.DeclaringType.FullName -eq $type.FullName } | ForEach-Object { $_.ToString() }
        $type.GetProperties() | Where-Object { $_.DeclaringType.FullName -eq $type.FullName } | ForEach-Object { $_.ToString() }
    }
    elseif ($typeName -eq "Microsoft.Extensions.AI.ChatResponse") {
        $type.GetConstructors() | ForEach-Object { $_.ToString() }
        $type.GetProperties() | Where-Object { $_.CanWrite } | ForEach-Object { $_.ToString() }
    }
    elseif ($typeName -eq "Microsoft.Extensions.AI.ChatMessage") {
        $type.GetConstructors() | ForEach-Object { $_.ToString() }
    }
    elseif ($typeName -eq "Microsoft.Extensions.AI.FunctionCallContent") {
        $type.GetConstructors() | ForEach-Object { $_.ToString() }
    }
}

