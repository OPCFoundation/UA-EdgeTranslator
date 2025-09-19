namespace Matter.Core
{
    class GlobalCounter
    {
        private static uint counter = 0;

        public static uint Counter => counter++;
    }
}
