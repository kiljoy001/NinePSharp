using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NinePSharp.Server.Utils;

public class AbiParser
{
    public struct FunctionCall
    {
        public string Name { get; set; }
        public string[] Arguments { get; set; }
    }

    /// <summary>
    /// Parses a function call string like "transfer(0x...,100)"
    /// </summary>
    public static FunctionCall? ParseCall(string callString)
    {
        var match = Regex.Match(callString, @"^(\w+)\((.*)\)$");
        if (!match.Success) return null;

        var name = match.Groups[1].Value;
        var argsStr = match.Groups[2].Value;
        
        var args = string.IsNullOrEmpty(argsStr) 
            ? Array.Empty<string>() 
            : argsStr.Split(',').Select(s => s.Trim()).ToArray();

        return new FunctionCall
        {
            Name = name,
            Arguments = args
        };
    }
}
