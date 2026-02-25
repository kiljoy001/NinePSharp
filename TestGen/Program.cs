using System;
using System.Reflection;
class Program {
    static void Main() {
        try {
            var method = typeof(NinePSharp.Generators.Generators.NinePArb).GetMethod("NinePMessage");
            var result = method.Invoke(null, null);
            Console.WriteLine("SUCCESS!");
        } catch (TargetInvocationException ex) {
            Console.WriteLine("CRASH: " + ex.InnerException);
        } catch (Exception ex) {
            Console.WriteLine("CRASH: " + ex);
        }
    }
}
