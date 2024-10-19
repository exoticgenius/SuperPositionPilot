using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using SPL;


var sc = new ServiceCollection();
sc.AddSingleton<IX, X>();



var sp = sc.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = false });

var s = sp.GetService<IX>();

var res =s.Z(":", 2);

if (!(res).Succeed(out string rx))
{
    Console.WriteLine("error captured");
}
else
{
    res.Faulted(out var f);
    return; //f;
}


Console.ReadLine();

public interface IX
{
    SPR<string> Z(string first, int z);
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
    public SPR<string> Z(string first, int z)
    {
        if (first == null) throw new ArgumentNullException("first");
        return x + first + z;
    }
}


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

