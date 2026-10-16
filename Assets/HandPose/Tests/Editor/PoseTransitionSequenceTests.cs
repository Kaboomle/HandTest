using NUnit.Framework;

namespace HandPose.Tests
{
    public class PoseTransitionSequenceTests
    {
        [Test]
        public void InactivityClearsHistoryAndPendingCandidateAtDeadline()
        {
            var sequence=new PoseTransitionSequence { InactivitySeconds=5 };
            sequence.Update(PoseId.Open,0); sequence.Update(PoseId.Open,.1); sequence.Update(PoseId.Open,.2);
            sequence.Update(PoseId.Open,4);
            sequence.Update(PoseId.Fist,5); sequence.Update(PoseId.Fist,5.1);
            Assert.That(sequence.Expire(5.19),Is.False);
            Assert.That(sequence.Expire(5.2),Is.True);
            Assert.That(sequence.States,Is.Empty);
            Assert.That(sequence.Update(PoseId.Fist,5.3),Is.False);
            Assert.That(sequence.Update(PoseId.Fist,5.4),Is.False);
            Assert.That(sequence.Update(PoseId.Fist,5.5),Is.True);
            CollectionAssert.AreEqual(new[] {PoseId.Fist},sequence.States);
        }

        [Test]
        public void ConfirmedChangesExtendDeadlineAndZeroDisablesTimeout()
        {
            var left=new PoseTransitionSequence { RequiredFrames=1, InactivitySeconds=5 };
            var right=new PoseTransitionSequence { RequiredFrames=1, InactivitySeconds=5 };
            left.Update(PoseId.Open,0); right.Update(PoseId.Open,0);
            left.Update(PoseId.Fist,4);
            Assert.That(left.Expire(5),Is.False);
            Assert.That(right.Expire(5),Is.True);
            left.InactivitySeconds=0;
            Assert.That(left.Expire(100),Is.False);
            left.InactivitySeconds=5;
            // A sample arriving after expiry must not match against old history.
            left.Update(PoseId.Open,101);
            Assert.That(left.MatchesSuffix(new[] {PoseId.Open,PoseId.Fist,PoseId.Open}),Is.False);
            CollectionAssert.AreEqual(new[] {PoseId.Open},left.States);
        }

        [Test]
        public void MatchesOnlyNewConfirmedTransitionsAndAllowsOverlappingMatches()
        {
            var sequence=new PoseTransitionSequence();
            var pattern=new[] {PoseId.Open,PoseId.Fist,PoseId.Open};
            int matches=0;
            foreach(var pose in new[] {PoseId.Open,PoseId.Fist,PoseId.Open,PoseId.Open,PoseId.Fist,PoseId.Open})
                for(int i=0;i<3;i++)
                    if(sequence.Update(pose) && sequence.MatchesSuffix(pattern)) matches++;
            Assert.That(matches,Is.EqualTo(2));
            Assert.That(sequence.MatchesSuffix(null),Is.False);
            Assert.That(sequence.MatchesSuffix(new PoseId[0]),Is.False);
            Assert.That(sequence.MatchesSuffix(new[] {PoseId.Open}),Is.False);
            Assert.That(sequence.MatchesSuffix(new[] {PoseId.Open,PoseId.Open}),Is.False);
            sequence.Clear();
            Assert.That(sequence.MatchesSuffix(pattern),Is.False);
        }

        [Test]
        public void ConfirmsOnlyOnThirdFrameAndCompressesRepeats()
        {
            var sequence=new PoseTransitionSequence();
            foreach(var pose in new[] {PoseId.Open,PoseId.Open,PoseId.Open,PoseId.Fist,PoseId.Fist,PoseId.Fist,PoseId.Open,PoseId.Open}) sequence.Update(pose);
            CollectionAssert.AreEqual(new[] {PoseId.Open,PoseId.Fist},sequence.States);
            sequence.Update(PoseId.Open); sequence.Update(PoseId.Open);
            CollectionAssert.AreEqual(new[] {PoseId.Open,PoseId.Fist,PoseId.Open},sequence.States);
        }

        [Test]
        public void RejectsShortCandidatesAndInterruptedTracking()
        {
            var sequence=new PoseTransitionSequence();
            foreach(var pose in new[] {PoseId.Open,PoseId.Open,PoseId.Open,PoseId.Fist,PoseId.Fist,PoseId.Open,PoseId.Fist,PoseId.Fist,PoseId.TrackingUnavailable,PoseId.Fist}) sequence.Update(pose);
            CollectionAssert.AreEqual(new[] {PoseId.Open},sequence.States);
            sequence.Update(PoseId.Fist); sequence.Update(PoseId.Fist);
            CollectionAssert.AreEqual(new[] {PoseId.Open,PoseId.Fist},sequence.States);
        }

        [Test]
        public void ClearStartsANewSequence()
        {
            var sequence=new PoseTransitionSequence();
            for(int i=0;i<3;i++) sequence.Update(PoseId.Open);
            sequence.Update(PoseId.Fist); sequence.Update(PoseId.Fist);
            sequence.Clear(); sequence.Update(PoseId.Fist);
            Assert.That(sequence.States,Is.Empty);
            sequence.Update(PoseId.Fist); sequence.Update(PoseId.Fist);
            CollectionAssert.AreEqual(new[] {PoseId.Fist},sequence.States);
        }
    }
}
