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
        var prms = new List<Type>();
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
