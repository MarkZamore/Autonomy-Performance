using System;using System.Windows;using System.Windows.Controls;
class QueuedScrollingTests {
 [STAThread]static void Main(){foreach(bool animated in new[]{false,true}) {
 var viewer=new ScrollViewer{Width=400,Height=300,Content=new Border{Height=3200}};
 viewer.Measure(new Size(400,300));viewer.Arrange(new Rect(0,0,400,300));viewer.UpdateLayout();
 using(var control=new SmoothScrollingController(viewer,()=>animated)){
 control.ScrollByWheelDelta(-120);control.ScrollByWheelDelta(-120);control.ScrollByWheelDelta(-60);
 if(control.TargetOffset!=90)throw new Exception("Queued input lost at animation="+animated);
 for(int i=0;i<100;i++){control.AdvanceFrame(1000d/60);viewer.UpdateLayout();}
 if(Math.Abs(viewer.VerticalOffset-90)>.01)throw new Exception("Scroll failed to settle");
 control.ScrollByWheelDelta(120);viewer.ScrollToVerticalOffset(200);viewer.UpdateLayout();
 for(int i=0;i<100;i++){control.AdvanceFrame(1000d/60);viewer.UpdateLayout();}
 if(viewer.VerticalOffset!=200)throw new Exception("Manual input lost");
 }}Console.WriteLine("PASS: queued scrolling with Windows animations on/off; system preference unchanged.");}
}
