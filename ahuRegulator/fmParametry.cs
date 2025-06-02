using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ahuRegulator
{
    public partial class fmParametry : Form
    {

        public double kp;
        public double ki;
        public double kp2;
        public double ki2;
        public double t1;
        public double t2;
        public double ch0;
        public double ch100;
        public double wch0;
        public double wch100;
        public double wci0;
        public double wci100;
        public double nag0;
        public double nag100;
        public double zWymP;
        public double zWymI;
        public double zNagP;
        public double zNagI;

        public double maxTempNawiewu;
        public double minTempNawiewu;

        public fmParametry()
        {
            InitializeComponent();
        }

        public void UstawKontrolki()
        {
            edKp.Text = kp.ToString();
            edKi.Text = ki.ToString();
            edKp2.Text = kp2.ToString();
            edKi2.Text = ki2.ToString();
            edT1.Text = t1.ToString();
            edT2.Text = t2.ToString();
            textBox1.Text = minTempNawiewu.ToString();
            textBox2.Text = maxTempNawiewu.ToString();
            textBox3.Text = ch100.ToString();
            textBox4.Text = ch0.ToString();
            textBox5.Text = wch100.ToString();
            textBox6.Text = wch0.ToString();
            textBox7.Text = wci100.ToString();
            textBox8.Text = wci0.ToString();
            textBox9.Text = nag100.ToString();
            textBox10.Text = nag0.ToString();
            textBox12.Text = zNagP.ToString();
            textBox11.Text = zNagI.ToString();
            textBox14.Text = zWymP.ToString();
            textBox13.Text = zWymI.ToString();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            try
            {
                kp = Convert.ToDouble(edKp.Text);
                ki = Convert.ToDouble(edKi.Text);
                kp2 = Convert.ToDouble(edKp2.Text);
                ki2 = Convert.ToDouble(edKi2.Text);
                t1 = Convert.ToDouble(edT1.Text);
                t2 = Convert.ToDouble(edT2.Text);
                maxTempNawiewu = Convert.ToDouble(textBox2.Text);
                minTempNawiewu = Convert.ToDouble(textBox1.Text);
                ch100 = Convert.ToDouble(textBox3.Text);
                ch0 = Convert.ToDouble(textBox4.Text);
                wch100 = Convert.ToDouble(textBox5.Text);
                wch0 = Convert.ToDouble(textBox6.Text);
                wci100 = Convert.ToDouble(textBox7.Text);
                wci0 = Convert.ToDouble(textBox8.Text);
                nag100 = Convert.ToDouble(textBox9.Text);
                nag0 = Convert.ToDouble(textBox10.Text);
                zNagP = Convert.ToDouble(textBox12.Text);
                zNagI = Convert.ToDouble(textBox11.Text);
                zWymP = Convert.ToDouble(textBox14.Text);
                zWymI = Convert.ToDouble(textBox13.Text);
                Close();
                DialogResult = DialogResult.OK;
            }
            catch
            {

            }

            
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Close();
            DialogResult = DialogResult.Cancel;
        }

        private void fmParametry_Load(object sender, EventArgs e)
        {
            UstawKontrolki();
        }
    }
}
