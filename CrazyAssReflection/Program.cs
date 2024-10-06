﻿using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
var x = new X();

Console.WriteLine(x.Z("1", 2));
Console.WriteLine(x.Z(null, 2));

RuntimeMethodModifier.ModifyMethodToAddTryCatch(typeof(X).GetMethod("Z")!);


var x2 = new X();

Console.WriteLine(x2.Z("1", 2));
Console.WriteLine(x2.Z(null, 2));

Console.ReadLine();

public class RuntimeMethodModifier
{
    public static void ModifyMethodToAddTryCatch(MethodInfo methodToModify)
    {
        var dynamicMethod = new DynamicMethod(
            methodToModify.Name,
            methodToModify.ReturnType,
            methodToModify.GetParameters().Select(x => x.ParameterType).ToArray(), // assuming no parameters for simplicity, modify for your needs
            methodToModify.DeclaringType
        );

        ILGenerator ilGen = dynamicMethod.GetILGenerator();

        // Begin the try block
        Label tryStart = ilGen.BeginExceptionBlock();
        Label tryEnd = ilGen.DefineLabel();
        Label catchStart = ilGen.DefineLabel();
        Label afterCatch = ilGen.DefineLabel();

        // Call the original method
        ilGen.Emit(OpCodes.Call, methodToModify);
        ilGen.Emit(OpCodes.Leave, afterCatch);

        // Begin the catch block
        ilGen.BeginCatchBlock(typeof(Exception));

        // Emit logic to return null when an exception occurs
        if (methodToModify.ReturnType.IsValueType)
        {
            LocalBuilder local = ilGen.DeclareLocal(methodToModify.ReturnType);
            ilGen.Emit(OpCodes.Ldloca, local);
            ilGen.Emit(OpCodes.Initobj, methodToModify.ReturnType);
            ilGen.Emit(OpCodes.Ldloc, local);
        }
        else
        {
            ilGen.Emit(OpCodes.Ldnull);
        }

        ilGen.Emit(OpCodes.Leave, afterCatch);

        // End try-catch block
        ilGen.EndExceptionBlock();

        // Mark the end label
        ilGen.MarkLabel(afterCatch);

        ilGen.Emit(OpCodes.Ret);

        var ps = methodToModify.GetParameters().Select(x => x.ParameterType).ToList();
        ps.Add(methodToModify.ReturnType);
        var f = GetProperFuncType(ps.Count);
        var del = f.MakeGenericType(ps.ToArray());


        // Complete the dynamic method
        var modifiedMethod = dynamicMethod.CreateDelegate(del);

        //use reflection to replace the original method body(requires unsafe code)
        ReplaceMethodBody(methodToModify, modifiedMethod);
    }

    public static Type GetProperFuncType(int c) => c switch
    {
        1 => typeof(Func<>),
        2 => typeof(Func<,>),
        3 => typeof(Func<,,>),
        4 => typeof(Func<,,,>),
        5 => typeof(Func<,,,,>),
        6 => typeof(Func<,,,,,>),
        7 => typeof(Func<,,,,,,>),
        8 => typeof(Func<,,,,,,,>),
        9 => typeof(Func<,,,,,,,,>),
        10 => typeof(Func<,,,,,,,,,>),
        11 => typeof(Func<,,,,,,,,,,>),
        12 => typeof(Func<,,,,,,,,,,,>),
        13 => typeof(Func<,,,,,,,,,,,,>),
        14 => typeof(Func<,,,,,,,,,,,,,>),
        15 => typeof(Func<,,,,,,,,,,,,,,>),
        16 => typeof(Func<,,,,,,,,,,,,,,,>),
        17 => typeof(Func<,,,,,,,,,,,,,,,,>),
        _ => throw new Exception()
    };


    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    public static void ReplaceMethodBody(MethodInfo methodToModify, Delegate newMethod)
    {
        // Get the method handle for the method to modify
        RuntimeMethodHandle methodHandle = methodToModify.MethodHandle;

        // Get the function pointer for the new method
        IntPtr newMethodPtr = GetMethodPointer(newMethod.Method);

        // Get the function pointer for the original method
        IntPtr methodPtr = GetMethodPointer(methodToModify);

        // Unprotect the memory region where the method body resides (required for modification)
        if (VirtualProtect(methodPtr, new UIntPtr((uint)IntPtr.Size), PAGE_EXECUTE_READWRITE, out uint oldProtect))
        {
            // Replace the function pointer with the new one
            unsafe
            {
                *(IntPtr*)methodPtr = newMethodPtr;
            }

            // Restore the memory protection to the original state
            VirtualProtect(methodPtr, new UIntPtr((uint)IntPtr.Size), oldProtect, out _);
        }
        else
        {
            throw new Exception("Failed to modify method body.");
        }
    }

    private static IntPtr GetMethodPointer(MethodInfo method)
    {
        // Ensure that the method is JIT-compiled and ready
        RuntimeHelpers.PrepareMethod(method.MethodHandle);

        // Return the function pointer for the method
        return method.MethodHandle.GetFunctionPointer();
    }
}

public class X
{
    public string Z(string first, int z)
    {
        return first + z;
    }
}

/// <summary>
/// super position result,
/// equivalents to a maybe result that can contain result data or exception at the same time,
/// and is not determinable until result qualification happens
/// </summary>
public struct SPR<T>
{
    private SPV<T> Value { get; }
    private SPF Fault { get; }

    public SPR(T val)
    {
        Value = new SPV<T>(val);
        Fault = default;
    }

    public SPR(in SPF fault)
    {
        Value = default;
        Fault = fault;
    }

    public bool Succeed() => Value.Completed;

    public bool Succeed(out T? result)
    {
        if (Value.Completed)
        {
            result = Value.Payload;
            return true;
        }

        result = default;
        return false;
    }

    public bool Faulted() => !Value.Completed;

    public bool Faulted(out SPF fault)
    {
        if (!Value.Completed)
        {
            fault = Fault;
            return true;
        }

        fault = default;
        return false;
    }

    public static implicit operator SPR<T>(in T val)
    {
        return new SPR<T>(val);
    }

    public static implicit operator SPR<T>(in SPF fault) =>
        new SPR<T>(fault);
}

/// <summary>
/// super position fault
/// </summary>
public struct SPF
{
    #region ' props '
    public LinkedList<Type>? CapturedContext { get; private set; }

    public object[]? Parameters { get; private set; }

    public Exception? Exception { get; private set; }

    public string? Message { get; private set; }
    #endregion ' props '

    #region ' ctors '
    public SPF() : this(default, default, default, default) { }

    public SPF(Type capturedContext) : this(capturedContext, default, default, default) { }

    public SPF(object[] parameters) : this(default, parameters, default, default) { }

    public SPF(Exception exception) : this(default, default, exception, default) { }

    public SPF(string message) : this(default, default, default, message) { }

    public SPF(Exception exception, string message) : this(default, default, exception, message) { }

    public SPF(object[] parameters, Exception exception) : this(default, parameters, exception, default) { }

    public SPF(object[] parameters, Exception exception, string message) : this(default, parameters, exception, message) { }

    public SPF(object[] parameters, string message) : this(default, parameters, default, message) { }

    public SPF(Type capturedContext, object[] parameters) : this(capturedContext, parameters, default, default) { }

    public SPF(Type capturedContext, Exception exception) : this(capturedContext, default, exception, default) { }

    public SPF(Type capturedContext, string message) : this(capturedContext, default, default, message) { }

    public SPF(Type capturedContext, Exception exception, string message) : this(capturedContext, default, exception, message) { }

    public SPF(Type capturedContext, object[] parameters, Exception exception) : this(capturedContext, parameters, exception, default) { }

    public SPF(Type capturedContext, object[] parameters, string message) : this(capturedContext, parameters, default, message) { }

    public SPF(Type? capturedContext, object[]? parameters, Exception? exception, string? message)
    {
        CapturedContext = capturedContext is not null ?
            new LinkedList<Type>([capturedContext]) :
            null;

        Parameters = parameters;
        Exception = exception;
        Message = message;
    }
    #endregion ' ctors '

    #region ' generators '
    public static SPF Gen() => new SPF();
    public static SPF Gen(Type capturedContext) => new SPF(capturedContext);
    public static SPF Gen(object[] parameters) => new SPF(parameters);
    public static SPF Gen(Exception exception) => new SPF(exception);
    public static SPF Gen(string message) => new SPF(message);
    public static SPF Gen(Exception exception, string message) => new SPF(exception, message);
    public static SPF Gen(object[] parameters, Exception exception) => new SPF(parameters, exception);
    public static SPF Gen(object[] parameters, string message) => new SPF(parameters, message);
    public static SPF Gen(object[] parameters, Exception exception, string message) => new SPF(parameters, exception, message);
    public static SPF Gen(Type capturedContext, object[] parameters) => new SPF(capturedContext, parameters);
    public static SPF Gen(Type capturedContext, Exception exception) => new SPF(capturedContext, exception);
    public static SPF Gen(Type capturedContext, string message) => new SPF(capturedContext, message);
    public static SPF Gen(Type capturedContext, Exception exception, string message) => new SPF(capturedContext, exception, message);
    public static SPF Gen(Type capturedContext, object[] parameters, Exception exception) => new SPF(capturedContext, parameters, exception);
    public static SPF Gen(Type capturedContext, object[] parameters, string message) => new SPF(capturedContext, parameters, message);
    public static SPF Gen(Type capturedContext, object[] parameters, Exception exception, string message) => new SPF(capturedContext, parameters, exception, message);
    #endregion ' generators '
}

/// <summary>
/// super position value
/// </summary>
public struct SPV<T>
{
    public bool Completed { get; set; }
    public T? Payload { get; set; }

    public SPV(bool completed = true)
    {
        Completed = completed;
        Payload = default;
    }

    public SPV(T payload)
    {
        Completed = true;
        Payload = payload;
    }
}