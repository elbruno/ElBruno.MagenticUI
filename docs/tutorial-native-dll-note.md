# ElBruno.MagenticUI — Native DLL Note

`ElBruno.LocalLLMs` depends on `Microsoft.ML.OnnxRuntimeGenAI`. Due to a known issue where
that package's `buildTransitive/` folder omits Windows targets, the native
`onnxruntime-genai.dll` is not automatically copied unless you reference the package directly.

`ElBruno.MagenticUI.App.csproj` already includes this direct reference. If you create a new
project using `ElBruno.LocalLLMs`, add:

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntimeGenAI" Version="0.14.1" />
```
