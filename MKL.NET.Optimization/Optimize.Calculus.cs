﻿using System;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MKLNET
{
    public static partial class Optimize
    {
        /// <summary>
        /// Finite-difference approximation of the derivative of a scalar function. The error is O(epsilon^2).
        /// </summary>
        /// <param name="f">The function of which to determine the derivative.</param>
        /// <param name="x">The value at which to determine the derivative.</param>
        /// <param name="epsilon">The small value used to increment and decrement x to determine the derivative.</param>
        /// <returns>The derivative of the function at x.</returns>
        public static double Derivative_Approx(Func<double, double> f, double x, double epsilon)
            => (f(x + epsilon) - f(x - epsilon)) * 0.5 / epsilon;

        /// <summary>
        /// Finite-difference approximation of the derivative of a scalar function. The function is called in parallel. The error is O(epsilon^2).
        /// </summary>
        /// <param name="f">The function of which to determine the derivative, called in parallel.</param>
        /// <param name="x">The value at which to determine the derivative.</param>
        /// <param name="epsilon">The small value used to increment and decrement x to determine the derivative.</param>
        /// <returns>The derivative of the function at x.</returns>
        public static double Derivative_Approx_Parallel(Func<double, double> f, double x, double epsilon)
        {
            var f0 = Task.Run(() => f(x - epsilon));
            return (f(x + epsilon) - f0.Result) * 0.5 / epsilon;
        }

        /// <summary>
        /// Finite-element approximatiom of the integral of a scalar function. The error is O(((b - a) / n)^2).
        /// </summary>
        /// <param name="n">The number of elements to divide the region.</param>
        /// <param name="f">The function of which to determine the integral.</param>
        /// <param name="a">The start of the region to integrate.</param>
        /// <param name="b">The end of the region to integrate.</param>
        /// <returns>The integral of the function from a to b.</returns>
        public static double Integral_Approx(int n, Func<double, double> f, double a, double b)
        {
            var total = (f(a) + f(b)) * 0.5;
            var h = (b - a) / n;
            for (int i = 1; i < n; i++) total += f(a + i * h);
            return total * h;
        }

        /// <summary>
        /// Finite-element approximatiom of the integral of a scalar function. The function is called in parallel. The error is O(((b - a) / n)^2).
        /// </summary>
        /// <param name="n">The number of elements to divide the region.</param>
        /// <param name="f">The function of which to determine the integral, called in parallel.</param>
        /// <param name="a">The start of the region to integrate.</param>
        /// <param name="b">The end of the region to integrate.</param>
        /// <returns>The integral of the function from a to b.</returns>
        public static double Integral_Approx_Parallel(int n, Func<double, double> f, double a, double b)
        {
            var total = 0.0;
            var h = (b - a) / n;
            var lockObject = new object();
            Parallel.For(-1, n, () => 0.0,
                (i, _, t) => t + (i > 0 ? f(a + i * h) : i == 0 ? f(b) * 0.5 : f(a) * 0.5),
                t => { lock (lockObject) total += t; });
            return total * h;
        }

        static IEnumerable<double> Derivative_Estimates(Func<double, double> f, double x, double epsilon)
        {
            while (true)
            {
                yield return Derivative_Approx(f, x, epsilon);
                epsilon *= 0.5;
            }
        }

        static IEnumerable<double> Derivative_Estimates_Parallel(Func<double, double> f, double x, double epsilon)
        {
            while (true)
            {
                yield return Derivative_Approx_Parallel(f, x, epsilon);
                epsilon *= 0.5;
            }
        }

        static IEnumerable<double> Integral_Estimates(Func<double, double> f, double a, double b)
        {
            var n = 1;
            var h = b - a;
            var estimate = (f(a) + f(b)) * 0.5;
            yield return estimate * h;
            while (true)
            {
                n *= 2;
                h = (b - a) / n;
                for (int i = 1; i < n; i += 2) estimate += f(a + i * h);
                yield return estimate * h;
            }
        }

        static IEnumerable<double> Integral_Estimates_Parallel(Func<double, double> f, double a, double b)
        {
            var n = 1;
            var h = b - a;
            var fb = Task.Run(() => f(b));
            var estimate = (f(a) + fb.Result) * 0.5;
            yield return estimate * h;
            var lockObject = new object();
            while (true)
            {
                n *= 2;
                h = (b - a) / n;
                Parallel.For(0, n / 2, () => 0.0,
                    (i, _, t) => t + f(a + (1 + i * 2) * h),
                    t => { lock (lockObject) estimate += t; });
                yield return estimate * h;
            }
        }

        static double Richardson_Extrapolation(double atol, double rtol, IEnumerable<double> estimates)
        {
            var prevDiff = double.PositiveInfinity;
            var e = estimates.GetEnumerator();
            e.MoveNext();
            var prevRow = new[] { e.Current };
            while (true)
            {
                e.MoveNext();
                var estimate = e.Current;
                var row = new double[prevRow.Length + 1];
                row[0] = estimate;
                var pow4 = 4.0;
                for (int i = 0; i < prevRow.Length; i++)
                {
                    row[i + 1] = estimate = (estimate * pow4 - prevRow[i]) / (pow4 - 1);
                    pow4 *= 4;
                }
                var c = row[row.Length - 1];
                var diff = Math.Abs(c - prevRow[prevRow.Length - 1]);
                var tol = atol + rtol * Math.Abs(c);
                if (prevDiff <= tol && diff <= tol) return c;
                prevDiff = diff;
                prevRow = row;
            }
        }

        /// <summary>
        /// Finite-difference approximation of the derivative of a scalar function accurate to a tolerance using Richardson extrapolation.
        /// The first Richardson estimate possible has error O(epsilon^6).
        /// </summary>
        /// <param name="atol">The absolute tolerance of the derivative required.</param>
        /// <param name="rtol">The relative tolerance of the derivative required.</param>
        /// <param name="f">The function of which to determine the derivative.</param>
        /// <param name="x">The value at which to determine the derivative.</param>
        /// <param name="epsilon">The starting small value used to increment and decrement x to determine the derivative.</param>
        /// <returns>The derivative of the function at x.</returns>
        public static double Derivative_Approx(double atol, double rtol, Func<double, double> f, double x, double epsilon)
            => Richardson_Extrapolation(atol, rtol, Derivative_Estimates(f, x, epsilon));

        /// <summary>
        /// Finite-difference approximation of the derivative of a scalar function accurate to a tolerance using Richardson extrapolation.
        /// The first Richardson estimate possible has error O(epsilon^6).
        /// </summary>
        /// <param name="atol">The absolute tolerance of the derivative required.</param>
        /// <param name="rtol">The relative tolerance of the derivative required.</param>
        /// <param name="f">The function of which to determine the derivative, called in parallel.</param>
        /// <param name="x">The value at which to determine the derivative.</param>
        /// <param name="epsilon">The starting small value used to increment and decrement x to determine the derivative.</param>
        /// <returns>The derivative of the function at x.</returns>
        public static double Derivative_Approx_Parallel(double atol, double rtol, Func<double, double> f, double x, double epsilon)
            => Richardson_Extrapolation(atol, rtol, Derivative_Estimates_Parallel(f, x, epsilon));

        /// <summary>
        /// Finite-element approximatiom of the integral of a scalar function accurate to a tolerance using Richardson extrapolation.
        /// </summary>
        /// <param name="atol">The absolute tolerance of the integral required.</param>
        /// <param name="rtol">The relative tolerance of the integral required.</param>
        /// <param name="f">The function of which to determine the integral.</param>
        /// <param name="a">The start of the region to integrate.</param>
        /// <param name="b">The end of the region to integrate.</param>
        /// <returns>The integral of the function from a to b.</returns>
        public static double Integral_Approx(double atol, double rtol, Func<double, double> f, double a, double b)
            => Richardson_Extrapolation(atol, rtol, Integral_Estimates(f, a, b));

        /// <summary>
        /// Finite-element approximatiom of the integral of a scalar function accurate to a tolerance using Richardson extrapolation.
        /// </summary>
        /// <param name="atol">The absolute tolerance of the integral required.</param>
        /// <param name="rtol">The relative tolerance of the integral required.</param>
        /// <param name="f">The function of which to determine the integral, called in parallel.</param>
        /// <param name="a">The start of the region to integrate.</param>
        /// <param name="b">The end of the region to integrate.</param>
        /// <returns>The integral of the function from a to b.</returns>
        public static double Integral_Approx_Parallel(double atol, double rtol, Func<double, double> f, double a, double b)
            => Richardson_Extrapolation(atol, rtol, Integral_Estimates_Parallel(f, a, b));

        public static bool Derivative_Check(double atol, double rtol, Func<double, double> f, Func<double, double> g, double x, double epsilon)
        {
            var expected = Derivative_Approx(atol, rtol, f, x, epsilon);
            return Math.Abs(expected - g(x)) < Tol(atol, rtol, expected);
        }
    }
}