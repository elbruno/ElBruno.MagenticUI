using System;
using System.Linq;
using System.Reflection;

var dllPath = @"C:\Users\brunocapuano\.nuget\packages\microsoft.extensions.ai.abstractions\10.8.3\lib\net9.0\Microsoft.Extensions.AI.Abstractions.dll";
var asm = Assembly.LoadFrom(dllPath);

PrintType(asm.GetType("Microsoft.Extensions.AI.IChatClient"));
PrintType(asm.GetType("Microsoft.Extensions.AI.ChatResponse"));
PrintType(asm.GetType("Microsoft.Extensions.AI.ChatMessage"));
PrintType(asm.GetType("Microsoft.Extensions.AI.FunctionCallContent"));

void PrintType(Type type)
{
    if (type == null) return;
    Console.WriteLine("--- " + type.FullName + " ---");
    
    if (type.FullName == "Microsoft.Extensions.AI.IChatClient")
    {
        foreach (var m in type.GetMethods().Where(m => m.DeclaringType == type)) Console.WriteLine(m.ToString());
        foreach (var p in type.GetProperties().Where(p => p.DeclaringType == type)) Console.WriteLine(p.ToString());
    }
    else if (type.FullName == "Microsoft.Extensions.AI.ChatResponse")
    {
        foreach (var c in type.GetConstructors()) Console.WriteLine(c.ToString());
        foreach (var p in type.GetProperties().Where(p => p.CanWrite)) Console.WriteLine(p.ToString());
    }
    else if (type.FullName == "Microsoft.Extensions.AI.ChatMessage" || type.FullName == "Microsoft.Extensions.AI.FunctionCallContent")
    {
        foreach (var c in type.GetConstructors()) Console.WriteLine(c.ToString());
    }
}
