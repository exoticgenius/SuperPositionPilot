using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Linq.Expressions;
var x = new X();


RuntimeMethodModifier.ModifyMethodToAddTryCatch(typeof(X).GetMethod("Z")!);


try
{
    var x2 = new X();

    Console.WriteLine(x2.Z(null, 456));

}
catch (Exception e)
{
    Console.WriteLine("dddd " + e.Message);
}
Console.ReadLine();
static class Method
{
    public static MethodInfo Of<TResult>(Expression<Func<TResult>> f) => ((MethodCallExpression)f.Body).Method;
    public static MethodInfo Of<T>(Expression<Action<T>> f) => ((MethodCallExpression)f.Body).Method;
    public static MethodInfo Of(Expression<Action> f) => ((MethodCallExpression)f.Body).Method;
}
public class RuntimeMethodModifier
{
    public static void ModifyMethodToAddTryCatch(MethodInfo methodToModify)
    {
        var prms = new List<Type>
        {
           // methodToModify.DeclaringType
        };
        prms.AddRange(methodToModify.GetParameters().Select(x => x.ParameterType).ToArray());

        var dynamicMethod = new DynamicMethod(
            methodToModify.Name,
            methodToModify.ReturnType,
            prms.ToArray(), 
            methodToModify.DeclaringType,
            true    
        );

        ILGenerator il = dynamicMethod.GetILGenerator();


        Label exBlock = il.BeginExceptionBlock();
        Label end = il.DefineLabel();
        il.DeclareLocal(methodToModify.ReturnType);

        il.Emit(OpCodes.Newobj, typeof(X).GetConstructors()[0]);
        for (int i = 0; i < prms.Count; i++)
            il.Emit(OpCodes.Ldarg, i);

        il.Emit(OpCodes.Call, methodToModify);
        //il.Emit(OpCodes.Ldstr, "123");







        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Leave_S, end);
        il.BeginCatchBlock(typeof(Exception));

        //il.Emit(OpCodes.Callvirt, typeof(Exception).GetMethod("get_Message"));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr,"caught");
        il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Leave_S, end);

        il.EndExceptionBlock();

        il.MarkLabel(end);
        il.Emit(OpCodes.Ldloc_0);
        il.Emit(OpCodes.Ret);
        

        var ps = methodToModify.GetParameters().Select(x => x.ParameterType).ToList();
        //ps.Insert(0,methodToModify.DeclaringType);
        ps.Add(methodToModify.ReturnType);
        var f = GetProperFuncType(ps.Count);
        var fp = GetProperFuncType(ps.Count - 1);
        var del = f.MakeGenericType(ps.ToArray());
        var delp = fp.MakeGenericType(ps[1 ..].ToArray());
        var genericArgs = delp.GetGenericArguments();

        // Complete the dynamic method
        var modifiedMethod = dynamicMethod.CreateDelegate(del);
        var res= modifiedMethod.Method.Invoke(new X(), ["123", 456]);
        //use reflection to replace the original method body(requires unsafe code)
        ReplaceMethodBody(methodToModify, modifiedMethod, genericArgs);


        //MethodReplacer.Replace(methodToModify, modifiedMethod.Method as DynamicMethod);
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

    public static void ReplaceMethodBody(MethodInfo methodToModify, Delegate newMethod, Type[] genericArgs)
    {
        // Get the method handle for the method to modify
        RuntimeMethodHandle methodHandle = methodToModify.MethodHandle;


        // Get the function pointer for the new method
        IntPtr newMethodPtr = MethodReplacer.GetDynamicHandle(newMethod.Method as DynamicMethod).GetFunctionPointer();

        // Get the function pointer for the original method
        IntPtr methodPtr = GetMethodPointer(methodToModify, genericArgs);

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

    private static IntPtr GetMethodPointer(MethodInfo method, Type[] genericArgs)
    {
        // Ensure that the method is JIT-compiled and ready
        RuntimeHelpers.PrepareMethod(method.MethodHandle, new RuntimeTypeHandle[0]);

        // Return the function pointer for the method
        return method.MethodHandle.GetFunctionPointer();
    }

    public static class MethodReplacer
    {
        public static unsafe void Replace(MethodInfo methodToReplace, DynamicMethod methodToInject)
        {
            // Get the MethodInfo for the original method
            var originalMethodInfo = methodToReplace;

            // Replace the original method with the dynamic method
            // Here, we're using a hacky way to replace the method using reflection
            // Note: This is generally discouraged and not a standard practice
            RuntimeHelpers.PrepareMethod(originalMethodInfo.MethodHandle);
            var dynamicMethodHandle = GetDynamicHandle(methodToInject);
            RuntimeHelpers.PrepareMethod(dynamicMethodHandle);

            unsafe
            {
                // Replace the original method's IL with the dynamic method's IL
                IntPtr originalMethodPtr = originalMethodInfo.MethodHandle.GetFunctionPointer();
                IntPtr dynamicMethodPtr = dynamicMethodHandle.GetFunctionPointer();
                Buffer.MemoryCopy((void*)dynamicMethodPtr, (void*)originalMethodPtr, sizeof(void*), sizeof(void*));
            }
        }

        public static RuntimeMethodHandle GetDynamicHandle(DynamicMethod dynamicMethod)
        {
            var descr = typeof(DynamicMethod)
                .GetMethod("GetMethodDescriptor", BindingFlags.Instance | BindingFlags.NonPublic);

            var res = (RuntimeMethodHandle)descr.Invoke(dynamicMethod, null);

            return res;
        }
    }
}

public class X
{
    public string Z(string v, int v1)
    {
        if (v == null) throw new ArgumentNullException("first");
        return v + v1 + "RRR";
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