using System;
using MyMediaLite.Eval.Measures;

namespace Optimization
{
	public class BruteForceOptimizer: IOptimizer<double, string>
	{
		private double bestFunctionValue;
		private double[] bestParameters;
		private bool reset = true;

		public void Reset()
		{
			reset = true;
		}
		public IOptimizerListener<double, string> OptimiserListener { get; set; }
		public void Iterate(double functionValue, double[] parameters)
		{
			if (reset)
			{
				reset = false;

				for (var i = 0; i < parameters.Length; i++)
				{
					parameters[i] = OptimizationLowerBound;
				}
			}

			var coefficient = Mode == OptimizationMode.Max ? 1 : -1;

			if (coefficient * functionValue > coefficient * bestFunctionValue)
			{
				bestParameters = parameters;
				bestFunctionValue = functionValue;
			}

			if (incrementArguments(parameters)) return;

		    OptimiserListener.OptimizationIncriment(parameters, PrecisionRecallAndF<string>.FMeasureAt);
		        //EvaluationMeasures.Measures.BionicNDCG.getNDCG);//Optimize for NDCG@5
		}

		private bool incrementArguments(double[] parameters)
		{
			for (var i = parameters.Length - 1; i > 0; i--)
			{
				if (parameters[i] >= OptimizationUpperBound)
				{
					parameters[i] = OptimizationLowerBound;

					if (i == 0)
					{
						OptimiserListener.OptimizationFinished(bestParameters, bestFunctionValue);
						return true;
					}
				}
				else
				{
					parameters[i] += OptimizationStep;
					break;
				}
			}

			return false;
		}

		public OptimizationMode Mode { get; set; }
		public double OptimizationStep { get; set; }
		public double OptimizationLowerBound { get; set; }
		public double OptimizationUpperBound { get; set; }
	}
}
