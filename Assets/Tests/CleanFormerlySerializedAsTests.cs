using NUnit.Framework;

namespace io.github.hatayama
{
    public class CleanFormerlySerializedAsTests
    {
        private FormerlySerializedAsRemover _cleaner;

        [SetUp]
        public void Setup()
        {
            _cleaner = new FormerlySerializedAsRemover();
        }

        [Test]
        public void TestSimpleFormerlySerializedAs()
        {
            string input = @"[FormerlySerializedAs(""_hgo"")]";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("", result);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void TestMultipleAttributes()
        {
            string input = @"[FormerlySerializedAs(""_hgo""), SerializeField]";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("[SerializeField]", result.Trim());
            Assert.AreEqual(1, count);
        }
        
        [Test]
        public void TestMultipleAttributes2()
        {
            string input = @"[FormerlySerializedAs(""_hgo3"")] [SerializeField]";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("[SerializeField]", result.Trim());
            Assert.AreEqual(1, count);
        }
        
        [Test]
        public void TestMultipleAttributes3()
        {
            string input = @"[FormerlySerializedAs(""_hgo3"")] [FormerlySerializedAs(""_xxx"")] [SerializeField]";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("[SerializeField]", result.Trim());
            Assert.AreEqual(2, count);
        }

        [Test]
        public void TestWithComment()
        {
            string input = @"[FormerlySerializedAs(""_hgo""), SerializeField] // コメント";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("[SerializeField] // コメント", result);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void TestComplexAttributes()
        {
            string input = @"[NotNull, FormerlySerializedAs(""_hgo""), SerializeField] // 複雑なコメント";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("[NotNull, SerializeField] // 複雑なコメント", result);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void TestMultipleLines()
        {
            string input = @"[FormerlySerializedAs(""_hgo"")]
[SerializeField]
private int value;";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("\n[SerializeField]\nprivate int value;".Replace("\r\n", "\n").Trim(), result.Replace("\r\n", "\n").Trim());
            Assert.AreEqual(1, count);
        }

        [Test]
        public void TestMultipleFormerlySerializedAs()
        {
            string input = @"[FormerlySerializedAs(""old1"")]
[FormerlySerializedAs(""old2"")]
[SerializeField]
private int value;";
            var (result, count) = _cleaner.RemoveFormerlySerializedAs(input);
            Assert.AreEqual("\n[SerializeField]\nprivate int value;".Replace("\r\n", "\n").Trim(), result.Replace("\r\n", "\n").Trim());
            Assert.AreEqual(2, count);
        }
    }
} 