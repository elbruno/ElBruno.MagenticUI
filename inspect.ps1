$dllPath = "C:\Users\brunocapuano\.nuget\packages\microsoft.extensions.ai.abstractions\10.8.3\lib\net9.0\Microsoft.Extensions.AI.Abstractions.dll"
$asm = [System.Reflection.Assembly]::LoadFrom($dllPath)

Write-Host "--- IChatClient ---"
$type = $asm.GetType("Microsoft.Extensions.AI.IChatClient")
if ($type) {
    $type.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | Where-Object { $_.DeclaringType.FullName -eq $type.FullName } | ForEach-Object { $_.ToString() }
    $type.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | Where-Object { $_.DeclaringType.FullName -eq $type.FullName } | ForEach-Object { $_.ToString() }
}

Write-Host "--- ChatResponse ---"
$type = $asm.GetType("Microsoft.Extensions.AI.ChatResponse")
if ($type) {
    $type.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | ForEach-Object { $_.ToString() }
    $type.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | Where-Object { $_.CanWrite } | ForEach-Object { $_.ToString() }
}

Write-Host "--- ChatMessage ---"
$type = $asm.GetType("Microsoft.Extensions.AI.ChatMessage")
if ($type) {
    $type.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | ForEach-Object { $_.ToString() }
}

Write-Host "--- FunctionCallContent ---"
$type = $asm.GetType("Microsoft.Extensions.AI.FunctionCallContent")
if ($type) {
    $type.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance) | ForEach-Object { $_.ToString() }
}
