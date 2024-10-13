using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;

var sc = new ServiceCollection();
sc.AddSingleton<IX, X>();






var sp = sc.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = false });

var s = sp.GetService<IX>();

var res =await s.Z(":", 2);

if (!(res).Succeed(out string rx))
{
    Console.WriteLine("error captured");
}
else
{
    res.Faulted(out var f);
    return; //f;
}

Console.WriteLine(rx);



public static class SP
{
    private static ConcurrentDictionary<Assembly, ModuleBuilder> ModuleBuilders;
    private static ConcurrentDictionary<Type, Type> TypeMapper;
    static SP()
    {
        ModuleBuilders = new();
        TypeMapper = new();
    }

    public static Type Extend<T>() =>
        Extend(typeof(T));

    public static Type? Extend(Type target)
    {
        if (!target
            .GetRuntimeMethods()
            .Any(x =>
                typeof(ISPR).IsAssignableFrom(x.ReturnType) ||
                    (typeof(Task).IsAssignableFrom(x.ReturnType) &&
                    x.ReturnType.IsGenericType &&
                    typeof(ISPR).IsAssignableFrom(x.ReturnType.GenericTypeArguments[0]))))
            return null;

        var module = ModuleBuilders
            .GetOrAdd(
                target.Assembly,
                (a) => AssemblyBuilder
                    .DefineDynamicAssembly(
                        new AssemblyName($"RuntimeAssembly_{a.FullName}"),
                        AssemblyBuilderAccess.Run)
                    .DefineDynamicModule("Module"));

        var typeName = $"RuntimeType_{Guid.NewGuid()}_{target.Name}";

        if (TypeMapper.TryGetValue(target, out var type))
            return type;

        var typeBuilder = module.DefineType(
            typeName,
            TypeAttributes.Public,
            target,
            target.GetInterfaces());

        foreach (var item in target.GetConstructors())
            GenerateConstructor(typeBuilder, item);

        foreach (var item in target
            .GetRuntimeMethods()
            .Where(x =>
                typeof(ISPR).IsAssignableFrom(x.ReturnType) ||
                    (typeof(Task).IsAssignableFrom(x.ReturnType) &&
                    x.ReturnType.IsGenericType &&
                    typeof(ISPR).IsAssignableFrom(x.ReturnType.GenericTypeArguments[0]))))
            GenerateMethod(item, typeBuilder);

        var ct = TypeMapper[target] = typeBuilder.CreateType()!;

        return ct;
    }

    private static void GenerateConstructor(TypeBuilder type, ConstructorInfo item)
    {
        var prms = new List<Type>();
        prms.AddRange(item.GetParameters().Select(x => x.ParameterType).ToArray());
        var ctor = type.DefineConstructor(item.Attributes, item.CallingConvention, prms.ToArray());
        var il = ctor.GetILGenerator();

        for (int i = 0; i < prms.Count + 1; i++)
            il.Emit(OpCodes.Ldarg, i);

        il.Emit(OpCodes.Call, item);
        il.Emit(OpCodes.Ret);
    }

    private static void GenerateMethod(MethodInfo methodinfo, TypeBuilder type)
    {
        var prms = new List<Type>();
        prms.AddRange(methodinfo.GetParameters().Select(x => x.ParameterType).ToArray());
        var returnType = methodinfo.ReturnType;
        var method = type.DefineMethod(
            methodinfo.Name,
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            CallingConventions.Standard | CallingConventions.HasThis,
            methodinfo.ReturnType,
            methodinfo.GetParameters().Select(x => x.ParameterType).ToArray());
        var il = method.GetILGenerator();

        Label exBlock = il.BeginExceptionBlock();
        Label end = il.DefineLabel();
        il.DeclareLocal(methodinfo.ReturnType); //      0
        il.DeclareLocal(typeof(Type)); //               1
        il.DeclareLocal(typeof(Exception)); //          2

        for (int i = 0; i < prms.Count + 1; i++)
            il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Call, methodinfo);

        if (typeof(Task).IsAssignableFrom(returnType))
        {
            il.Emit(OpCodes.Call, typeof(SP).GetMethods().First(x => x.Name == "SuppressTask").MakeGenericMethod(returnType.GenericTypeArguments[0].GenericTypeArguments));
        }
        else if (typeof(ValueTask).IsAssignableFrom(returnType))
        {

            il.Emit(OpCodes.Call, typeof(SP).GetMethods().First(x => x.Name == "SuppressValueTask").MakeGenericMethod(returnType.GenericTypeArguments[0].GenericTypeArguments));
        }

        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Leave_S, end);
        il.BeginCatchBlock(typeof(Exception));

        il.Emit(OpCodes.Stloc_2);
        il.Emit(OpCodes.Ldtoken, methodinfo.DeclaringType!);
        il.Emit(OpCodes.Ldloc_1); // 1 => 0


        il.Emit(OpCodes.Ldc_I4, prms.Count);
        il.Emit(OpCodes.Newarr, typeof(object)); // => 1

        for (int i = 0; i < prms.Count; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Ldarg, i + 1);
            if (prms[i].IsValueType)
                il.Emit(OpCodes.Box, prms[i]);
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Ldloc_2); //2 => 2

        il.Emit(OpCodes.Newobj, typeof(SPF).GetConstructor(new Type[] { typeof(Type), typeof(object[]), typeof(Exception) })!);


        if (typeof(Task).IsAssignableFrom(returnType))
        {
            il.Emit(OpCodes.Newobj, returnType.GenericTypeArguments[0].GetConstructor(new Type[] { typeof(SPF) })!);
            il.Emit(OpCodes.Call, typeof(Task).GetRuntimeMethods().First(x => x.Name == "FromResult").MakeGenericMethod(returnType.GenericTypeArguments[0]));

        }
        else if (typeof(ValueTask).IsAssignableFrom(returnType))
        {
            il.Emit(OpCodes.Newobj, typeof(SPR<>).MakeGenericType(returnType.GenericTypeArguments[0].GenericTypeArguments).GetConstructor(new Type[] { typeof(SPF) })!);
            il.Emit(OpCodes.Call, typeof(ValueTask<>).MakeGenericType(typeof(SPR<>).MakeGenericType(returnType.GenericTypeArguments[0].GenericTypeArguments)).GetConstructor(new Type[] { typeof(SPR<>).MakeGenericType(returnType.GenericTypeArguments[0].GenericTypeArguments) }));
        }
        else
        {
            il.Emit(OpCodes.Newobj, methodinfo.ReturnType.GetConstructor(new Type[] { typeof(SPF) })!);
        }
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Leave_S, end);

        il.EndExceptionBlock();

        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
    }

    public static async Task<SPR<T>> SuppressTask<T>(Task<SPR<T>> input)
    {
        try
        {
            return await input;
        }
        catch (Exception ex)
        {
            return new SPR<T>(new SPF(ex));
        }
    }
    public static async ValueTask<SPR<T>> SuppressValueTask<T>(ValueTask<SPR<T>> input)
    {
        try
        {
            return await input;
        }
        catch (Exception ex)
        {
            return new SPR<T>(new SPF(ex));
        }
    }

    public static T Gen<T>(params object?[]? @params) =>
        SP<T>.Gen(@params);

    public static SPR<T> Sup<T>(Func<T> function)
    {
        try
        {
            return function();
        }
        catch (Exception e)
        {
            return new SPR<T>(new SPF(e));
        }
    }
}

public static class SP<T>
{
    private static Type GeneratedType = SP.Extend<T>();

    public static T Gen(params object?[]? @params)
    {
        return (T)Activator.CreateInstance(GeneratedType, @params)!;
    }
}


//[DllImport("kernel32.dll", SetLastError = true)]
//static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

//const uint PAGE_EXECUTE_READWRITE = 0x40;

//static void ReplaceMethodBody(MethodInfo methodToModify, MethodInfo newMethod)
//{
//    // Get the method handle for the method to modify
//    RuntimeMethodHandle methodHandle = methodToModify.MethodHandle;


//    // Get the function pointer for the new method
//    IntPtr newMethodPtr = GetMethodPointer(newMethod);

//    // Get the function pointer for the original method
//    IntPtr methodPtr = GetMethodPointer(methodToModify);

//    // Unprotect the memory region where the method body resides (required for modification)
//    if (VirtualProtect(methodPtr, new UIntPtr((uint)IntPtr.Size), PAGE_EXECUTE_READWRITE, out uint oldProtect))
//    {
//        // Replace the function pointer with the new one
//        unsafe
//        {
//            Buffer.MemoryCopy((void*)newMethodPtr, (void*)methodPtr, sizeof(void*), sizeof(void*));
//        }

//        // Restore the memory protection to the original state
//        VirtualProtect(methodPtr, new UIntPtr((uint)IntPtr.Size), oldProtect, out _);
//    }
//    else
//    {
//        throw new Exception("Failed to modify method body.");
//    }
//}

//static IntPtr GetMethodPointer(MethodInfo method)
//{
//    // Ensure that the method is JIT-compiled and ready
//    RuntimeHelpers.PrepareMethod(method.MethodHandle);

//    // Return the function pointer for the method
//    return method.MethodHandle.GetFunctionPointer();
//}

public interface IX
{
    Task<SPR<string>> Z(string first, int z);
}
public class X : IX
{
    public X()
    {

    }
    private int x = 2;
    private IServiceProvider psp;
    public X(IServiceProvider sp)
    {
        psp = sp;
        x = 3;
    }
    public async Task<SPR<string>> Z(string first, int z)
    {
        await Task.Delay(1000);
        if (first == null) throw new ArgumentNullException("first");
        return x + first + z;
    }
}

public interface ISPR;
/// <summary>
/// super position result,
/// equivalents to a maybe result that can contain result data or exception at the same time,
/// and is not determinable until result qualification happens
/// </summary>
public struct SPR<T> : ISPR
{
    private SPV<T> Value { get; }
    private SPF Fault { get; }

    public SPR(T val)
    {
        Value = new SPV<T>(val);
        Fault = default;
    }

    public SPR(SPF fault)
    {
        Value = default;
        Fault = fault;
    }

    public bool Succeed() => Value.Completed;

    public bool Succeed(out T result)
    {
        if (Value.Completed)
        {
            result = Value.Payload;
            return true;
        }

        result = default!;
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

    public static implicit operator SPR<T>(in T val) =>
        new SPR<T>(val);

    public static implicit operator SPR<T>(in SPF fault) =>
        new SPR<T>(fault);
}

/// <summary>
/// super position fault
/// </summary>
public struct SPF
{
    #region ' props '
    public LinkedList<Type>? CapturedContext { get; }

    public object[]? Parameters { get; }

    public Exception? Exception { get; }

    public string? Message { get; }
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
            default;

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
    public bool Completed { get; }
    public T Payload { get; }

    public SPV() : this(false, default!) { }

    public SPV(bool completed) : this(completed, default!) { }

    public SPV(T payload) : this(true, payload) { }

    public SPV(bool compeleted, T payload)
    {
        Completed = compeleted;
        Payload = payload;
    }

    public SPV<T> DoneSPV() =>
        new SPV<T>(true);

    public SPV<T> UndoneSPV() =>
        new SPV<T>(false);
}
