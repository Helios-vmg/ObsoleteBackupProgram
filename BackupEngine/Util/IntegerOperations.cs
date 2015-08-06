namespace BackupEngine.Util
{
    public static class IntegerOperations
    {
        public static uint UncheckedToUint32(int i)
        {
            uint ret;
            unchecked
            {
                ret = (uint) i;
            }
            return ret;
        }
    }
}
