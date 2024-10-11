// See https://aka.ms/new-console-template for more information
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

Console.WriteLine("Hello, World!");

Extend(typeof(X));

Console.ReadLine();


static void Extend(Type pluginType)
{

    var assemblyName = Guid.NewGuid().ToString();
    var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
    var module = assembly.DefineDynamicModule("Module");
    var type = module.DefineType("WrapperImplementation_" + pluginType.Name, TypeAttributes.Public, pluginType);

    var pm = pluginType.GetMethod("Z");

    GenerateMethod(pm, type);

    var ct = type.CreateType();
    var met = ct.GetMethod(pm.Name);
    var res = met.Invoke(Activator.CreateInstance(ct), ["1", 456]);


    Console.ReadLine();
}

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

const uint PAGE_EXECUTE_READWRITE = 0x40;

static void ReplaceMethodBody(MethodInfo methodToModify, MethodInfo newMethod)
{
    // Get the method handle for the method to modify
    RuntimeMethodHandle methodHandle = methodToModify.MethodHandle;


    // Get the function pointer for the new method
    IntPtr newMethodPtr = GetMethodPointer(newMethod);

    // Get the function pointer for the original method
    IntPtr methodPtr = GetMethodPointer(methodToModify);

    // Unprotect the memory region where the method body resides (required for modification)
    if (VirtualProtect(methodPtr, new UIntPtr((uint)IntPtr.Size), PAGE_EXECUTE_READWRITE, out uint oldProtect))
    {
        // Replace the function pointer with the new one
        unsafe
        {
            Buffer.MemoryCopy((void*)newMethodPtr, (void*)methodPtr, sizeof(void*), sizeof(void*));
        }

        // Restore the memory protection to the original state
        VirtualProtect(methodPtr, new UIntPtr((uint)IntPtr.Size), oldProtect, out _);
    }
    else
    {
        throw new Exception("Failed to modify method body.");
    }
}

static IntPtr GetMethodPointer(MethodInfo method)
{
    // Ensure that the method is JIT-compiled and ready
    RuntimeHelpers.PrepareMethod(method.MethodHandle);

    // Return the function pointer for the method
    return method.MethodHandle.GetFunctionPointer();
}

static void GenerateMethod(MethodInfo pm, TypeBuilder type)
{
    var prms = new List<Type>();
    prms.AddRange(pm.GetParameters().Select(x => x.ParameterType).ToArray());

    var method = type.DefineMethod(
        pm.Name,
        MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual,
        CallingConventions.Standard | CallingConventions.HasThis,
        pm.ReturnType,
        pm.GetParameters().Select(x => x.ParameterType).ToArray());
    var il = method.GetILGenerator();
    //var impl = pm.GetMethodBody().GetILAsByteArray();

    Label exBlock = il.BeginExceptionBlock();
    Label end = il.DefineLabel();
    il.DeclareLocal(pm.ReturnType); //      0
    il.DeclareLocal(typeof(Type)); //       1
    il.DeclareLocal(typeof(Exception)); //  2

    for (int i = 0; i < prms.Count + 1; i++)
        il.Emit(OpCodes.Ldarg, i);

    il.Emit(OpCodes.Call, pm);



    il.Emit(OpCodes.Stloc_0);
    il.Emit(OpCodes.Leave_S, end);
    il.BeginCatchBlock(typeof(Exception));

    il.Emit(OpCodes.Stloc_2);
    il.Emit(OpCodes.Stloc_1, pm.DeclaringType);
    il.Emit(OpCodes.Ldloc_1);


    il.Emit(OpCodes.Ldc_I4, prms.Count);
    il.Emit(OpCodes.Newarr, typeof(object));
    
    //for (int i = 0; i < prms.Count; i++)
    //{
    //    il.Emit(OpCodes.Dup);
    //    il.Emit(OpCodes.Ldc_I4, i);
    //    il.Emit(OpCodes.Ldarg, i + 1);
    //    il.Emit(OpCodes.Stelem_Ref, typeof(object));
    //}

    il.Emit(OpCodes.Ldloc_2); //2

    il.Emit(OpCodes.Newobj, typeof(SPF).GetConstructor([typeof(Type), typeof(object[]), typeof(Exception)])!);
    il.Emit(OpCodes.Newobj, pm.ReturnType.GetConstructor([typeof(SPF)])!);
    il.Emit(OpCodes.Stloc_0);
    il.Emit(OpCodes.Leave_S, end);

    il.EndExceptionBlock();

    il.MarkLabel(end);
    il.Emit(OpCodes.Ldloc_0);
    il.Emit(OpCodes.Ret);
}

public class X
{
    private int x = 2;
    public SPR<(string, int)> Z(string first, int z)
    {
        if (first == null) throw new ArgumentNullException("first");
        return (x + first + z, x);
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

    public SPR(SPF fault)
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