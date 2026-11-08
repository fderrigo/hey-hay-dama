using System.Reflection.Emit;
using System.Diagnostics.CodeAnalysis;

namespace Ametista;

// Specialize execution of our existing manual graph; never fit or alter weights.
// API reference: https://learn.microsoft.com/dotnet/fundamentals/reflection/how-to-define-and-execute-dynamic-methods
internal static class InferenceCompiler
{
    internal delegate Estimate Inference(ReadOnlySpan<float> inputs);

    [RequiresDynamicCode("Emits the already-defined manual neural graph as IL.")]
    internal static Inference Compile(Weights weights)
    {
        var method=new DynamicMethod("AmetistaManualGraph",typeof(Estimate),[typeof(ReadOnlySpan<float>)],typeof(InferenceCompiler).Module);
        var il=method.GetILGenerator();
        var values=Enumerable.Range(0,weights.Inputs+weights.Nodes.Length).Select(_=>il.DeclareLocal(typeof(float))).ToArray();
        var value=il.DeclareLocal(typeof(float));var risk=il.DeclareLocal(typeof(float));
        var item=typeof(ReadOnlySpan<float>).GetProperty("Item")!.GetMethod!;
        var maximum=typeof(Math).GetMethod(nameof(Math.Max),[typeof(float),typeof(float)])!;
        var round=typeof(MathF).GetMethod(nameof(MathF.Round),[typeof(float)])!;
        for(int i=0;i<weights.Inputs;i++) {
            il.Emit(OpCodes.Ldarga_S,(byte)0);il.Emit(OpCodes.Ldc_I4,i);
            il.Emit(OpCodes.Call,item);il.Emit(OpCodes.Ldind_R4);il.Emit(OpCodes.Stloc,values[i]);
        }
        il.Emit(OpCodes.Ldc_R4,0f);il.Emit(OpCodes.Stloc,value);
        il.Emit(OpCodes.Ldc_R4,0f);il.Emit(OpCodes.Stloc,risk);
        for(int n=0;n<weights.Nodes.Length;n++) {
            var node=weights.Nodes[n];var activation=values[weights.Inputs+n];
            il.Emit(OpCodes.Ldc_R4,node.Bias);
            for(int j=0;j<node.From.Length;j++) {
                il.Emit(OpCodes.Ldloc,values[node.From[j]]);il.Emit(OpCodes.Ldc_R4,node.Weights[j]);
                il.Emit(OpCodes.Mul);il.Emit(OpCodes.Add);
            }
            il.Emit(OpCodes.Stloc,activation);
            // Preserve both floating-point operation order and Max argument order.
            il.Emit(OpCodes.Ldc_R4,0f);il.Emit(OpCodes.Ldloc,activation);
            il.Emit(OpCodes.Call,maximum);il.Emit(OpCodes.Stloc,activation);
            Head(value,node.ValueWeight);Head(risk,node.RiskWeight);
            void Head(LocalBuilder output,float coefficient) {
                il.Emit(OpCodes.Ldloc,output);il.Emit(OpCodes.Ldloc,activation);
                il.Emit(OpCodes.Ldc_R4,coefficient);il.Emit(OpCodes.Mul);
                il.Emit(OpCodes.Add);il.Emit(OpCodes.Stloc,output);
            }
        }
        il.Emit(OpCodes.Ldloc,value);il.Emit(OpCodes.Call,round);il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldloc,risk);il.Emit(OpCodes.Call,round);il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Newobj,typeof(Estimate).GetConstructor([typeof(int),typeof(int)])!);
        il.Emit(OpCodes.Ret);
        return (Inference)method.CreateDelegate(typeof(Inference));
    }
}
