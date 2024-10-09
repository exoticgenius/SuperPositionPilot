// See https://aka.ms/new-console-template for more information
using System.Reflection.Emit;
using System.Reflection;
using System.Runtime.CompilerServices;

Console.WriteLine("Hello, World!");

Extend(typeof(X).GetMethod(nameof(X.Z)));




static void Extend(MethodInfo pm)
{
    Type pluginType = pm.DeclaringType!;
    var assemblyName = Guid.NewGuid().ToString();
    var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run);
    var module = assembly.DefineDynamicModule("Module");
    var type = module.DefineType("WrapperImplementation_" + pluginType.Name, TypeAttributes.Public);

    var method = type.DefineMethod(
        pm.Name,
        MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.Virtual,
        CallingConventions.Standard | CallingConventions.HasThis,
        pm.ReturnType,
        pm.GetParameters().Select(x => x.ParameterType).ToArray());
    var methodGen = method.GetILGenerator();
    //var impl = pm.GetMethodBody().GetILAsByteArray();
    






    methodGen.Emit(OpCodes.Call, pm.);








    var ct = type.CreateType();
    var met = ct.GetMethod(pm.Name);
    RuntimeHelpers.PrepareMethod(met.MethodHandle);
}

public class X
{
    public string Z(string first, int z)
    {
        //if(first == null)throw new ArgumentNullException("first");
        return first + z;
    }
}