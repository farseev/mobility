using System;
using System.Diagnostics;
using EvaluationMeasures.Measures;
using MyMediaLite.Eval.Measures;

namespace Optimization
{
	public class HillClimbingOptimizer:IOptimizer<double, string>
	{
		private double bestFunctionValue;
		private double[] bestParameters;
		private bool reset = true;
		private int numberOfFaults = 0;
		private int lastIncrementedIndex = -1;

		private Random rnd;

		public int Seed { get; set; }
		public void Reset()
		{
			reset = true;
		}
		public IOptimizerListener<double, string> OptimiserListener { get; set; }
		public void Iterate(double functionValue, double[] parameters)
		{
			try
			{
				var coefficient = Mode == OptimizationMode.Max ? 1 : -1;

				var improved = coefficient*functionValue > coefficient*bestFunctionValue;

				if (reset)
				{
					reset = false;

					rnd = new Random(Seed);

					var maxRand = (int)((OptimizationUpperBound - OptimizationLowerBound) / OptimizationStep);

					for (var i = 0; i < parameters.Length; i++)
					{
						parameters[i] = rnd.Next(0, maxRand) * OptimizationStep;
					}

					//parameters[0] = 1;

					lastIncrementedIndex = parameters.Length - 1;
					improved = true;
				}

				if (improved)
				{
					numberOfFaults = 0;
					bestParameters = parameters;
					bestFunctionValue = functionValue;
				}
				else
				{
					numberOfFaults++;
				}

				if (incrementArguments(parameters, lastIncrementedIndex, improved))
				{
					OptimiserListener.OptimizationFinished(bestParameters, bestFunctionValue);
					return;
				}

                OptimiserListener.OptimizationIncriment(parameters, PrecisionRecallAndF<string>.FMeasureAt);//Optimize for NDCG@5
			}
			catch (Exception exc)
			{
				Debug.Write(exc);
			}
		}

		private bool incrementArguments(double[] parameters, int index, bool increment)
		{
			if (increment)
			{
				if (index > parameters.Length)
				{
					return true;
				}

				if (parameters[index] < OptimizationUpperBound)
				{
					parameters[index] += OptimizationStep;
				}
				else
				{
					if (numberOfFaults != parameters.Length)
					{
						return incrementArguments(parameters, index + 1, true);
					}

					return true;
				}

				lastIncrementedIndex = index;
			}
			else
			{
				parameters[lastIncrementedIndex] -= OptimizationStep;
				//We need last incremented index to revert changes if performance decrease

				return numberOfFaults == parameters.Length ||
						incrementArguments(parameters, lastIncrementedIndex - 1, true) ||
						lastIncrementedIndex == 0;
			}
			
			return false;
		}

		public OptimizationMode Mode { get; set; }
		public double OptimizationStep { get; set; }
		public double OptimizationLowerBound { get; set; }
		public double OptimizationUpperBound { get; set; }
	}
}
