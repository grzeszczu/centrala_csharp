using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ahuKlasy; // Assuming this contains cDaneWeWy and eZmienne
// If fmParametry is in a different namespace, you might need:
// using System.Windows.Forms; // For DialogResult, if fmParametry uses it.

namespace ahuRegulator
{

    #region ParametryLokalne

    // Example implementation - to be modified by the student
    class cRegulatorPI
    {
        double Ts = 1; // Sampling time, matches cRegulator.Ts
        public double calka = 0; // Integral sum
        public double max = 20;  // Max output limit
        public double min = -20; // Min output limit

        public double kp = 1; // Proportional gain
        public double ki = 0; // Integral gain
        public cRegulatorPI(double min, double max, double P, double I)
        {
            this.min = min;
            this.max = max;
            this.kp = P;
            this.ki = I;
        }
        public double Wyjscie(double Uchyb) // Output based on Error
        {
            calka = calka + Uchyb * Ts;
            double wyjscie = kp * Uchyb + ki * calka;

            // Anti-windup logic
            if (wyjscie > max)
            {
                if (ki > 0) // Anti-windup only if integral action is present
                    calka -= (wyjscie - max) / ki; // Adjust integral sum
                wyjscie = max;
            }
            if (wyjscie < min)
            {
                if (ki > 0) // Anti-windup
                    calka -= (wyjscie - min) / ki; // Adjust integral sum
                wyjscie = min;
            }
            return wyjscie;
        }
    }

    #endregion


    public enum eStanyPracyCentrali
    {
        Stop = 0,
        Praca = 1,
        RozruchWentylatora = 2,
        WychladzanieNagrzewnicy = 3,
        OpoznianieStartuWentylatora = 4 // NOWY STAN
        // SzybkieGrzanie (FastHeating) was removed - this simplifies logic
    }


    public class cRegulator
    {

        public cDaneWeWy DaneWejsciowe = null; // Input data object
        public cDaneWeWy DaneWyjsciowe = null; // Output data object
        public double Ts = 1; // Sampling time in seconds for the main regulator loop


        // Primary PI: Calculates desired supply air temp.
        // PDF: Heater outlet 20°C, Cooler outlet 22°C. Min/Max of 18-26°C is a reasonable target range for supply air.
        cRegulatorPI RegPI = new cRegulatorPI(18, 26, 8, 10);

        // Secondary PI: Controls heating/cooling/recovery based on supply air temp error.
        // Output range -7 to 7 is an intermediate control value.
        cRegulatorPI RegPI2 = new cRegulatorPI(-7, 7, 8, 8);

        // PI for Heater Safety: Protects heater based on exhaust air temp. Output 0-100%.
        cRegulatorPI RegPIzabNagrzewnicy = new cRegulatorPI(0, 100, 1, 1);

        // PI for Heat Exchanger Safety (Frost Protection): Protects exchanger based on temp after recovery. Output 0-100%.
        cRegulatorPI RegPIzabWymiennika = new cRegulatorPI(0, 100, 1, 1);

        eStanyPracyCentrali StanPracyCentrali = eStanyPracyCentrali.Stop; // Initial state

        double CzasOdStartu = 0; // Time since start (used for multiple delays)
        double CzasOdStopu = 0;  // Time since stop command

        double OpoznienieStartuWentylatora_s = 2.0; // NOWA ZMIENNA: Opóźnienie startu wentylatora (s)
        double OpoznienieZalaczeniaNagrzewnicy_s = 5; // Delay for heater activation after fan start (s)
        double OpoznienieWylaczeniaWentylatora_s = 10; // Delay for fan stop after AHU stop (s) (for cooldown)

        // Thresholds for RegPI2 output to activate cooler/heater
        double zalaczenieChlodnicy = -3; // If RegPI2 output < this, activate cooler
        double zalaczenieNagrzewnicy = 3;  // If RegPI2 output > this, activate heater

        // Thresholds for RegPI2 output to modulate heat exchanger (cooling contribution)
        double wymiennikChlodu_min = -2;  // Start modulating recovery for cooling
        double wymiennikChlodu_max = -6;  // Maximize recovery for cooling (or bypass if conditions met)

        // Thresholds for RegPI2 output to modulate heat exchanger (heating contribution)
        double wymiennikCiepla_min = 2;   // Start modulating recovery for heating
        double wymiennikCiepla_max = 6;   // Maximize recovery for heating

        // Setpoint for heater safety PI controller (e.g., min exhaust air temperature)
        // Using exhaust air temp (TempWyrzutni_C) as PV.
        double ZadanaTempCzynnika = 2; // °C 

        // Setpoint for heat exchanger frost protection PI controller (min temp after recovery)
        // PDF: Winter supply after recovery 15.6°C from -18°C inlet. 2°C is a safe frost protection setpoint.
        double ZadanaTempZaWymiennikiem = 2; // °C 


        public int iWywolanie() // Main regulation cycle method
        {
            // Read input values
            double t_zad = DaneWejsciowe.Czytaj(eZmienne.TempZadana_C); // Room temperature setpoint
            double t_pom = DaneWejsciowe.Czytaj(eZmienne.TempPomieszczenia_C); // Actual room temperature
            double t_naw = DaneWejsciowe.Czytaj(eZmienne.TempNawiewu_C); // Actual supply air temperature
            bool boStart = DaneWejsciowe.Czytaj(eZmienne.PracaCentrali) > 0; // AHU start/stop command

            // Initialize output values
            double y_nagrz = 0; // Heater control signal (0-100%)
            double y_chlod = 0; // Cooler control signal (0-100%)
            double y_wymiennika = 0; // Heat exchanger control signal (0% = full bypass, 100% = full recovery)

            bool boPracaWentylatoraNawiewu = false; // Supply fan status

            switch (StanPracyCentrali)
            {
                case eStanyPracyCentrali.Stop:
                    {
                        y_nagrz = 0;
                        y_chlod = 0;
                        y_wymiennika = 0; // Full bypass in Stop state
                        boPracaWentylatoraNawiewu = false;
                        CzasOdStartu = 0;
                        CzasOdStopu = 0;
                        RegPI.calka = 0; // Reset integral for PI controllers
                        RegPI2.calka = 0;
                        RegPIzabNagrzewnicy.calka = 0;
                        RegPIzabWymiennika.calka = 0;
                        if (boStart)
                        {
                            StanPracyCentrali = eStanyPracyCentrali.OpoznianieStartuWentylatora; // ZMIANA: Przejście do nowego stanu
                            CzasOdStartu = 0; // Resetuj timer dla opóźnienia startu wentylatora
                        }
                        break;
                    }

                case eStanyPracyCentrali.OpoznianieStartuWentylatora: // NOWY STAN
                    {
                        boPracaWentylatoraNawiewu = false; // Wentylator jest WYŁĄCZONY
                        y_nagrz = 0;
                        y_chlod = 0;
                        y_wymiennika = 0; // Bypass otwarty, brak odzysku na tym etapie

                        if (!boStart) // Jeśli sygnał startu zniknie w trakcie tego opóźnienia
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Stop;
                            // CzasOdStartu jest już 0 lub zostanie zresetowany w Stop
                            break;
                        }

                        if (CzasOdStartu < OpoznienieStartuWentylatora_s - Ts)
                        {
                            CzasOdStartu += Ts;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.RozruchWentylatora;
                            CzasOdStartu = 0; // Resetuj timer dla opóźnienia załączenia nagrzewnicy
                        }
                        break;
                    }

                case eStanyPracyCentrali.RozruchWentylatora:
                    {
                        if (!boStart)
                        {
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                            // Ustaw bezpieczne wartości wyjść przed przejściem do WychladzanieNagrzewnicy
                            y_nagrz = 0;
                            y_chlod = 0;
                            y_wymiennika = 0; // Pełny bypass
                                              // boPracaWentylatoraNawiewu zostanie obsłużone przez WychladzanieNagrzewnicy
                            break;
                        }

                        boPracaWentylatoraNawiewu = true; // Wentylator ZAŁĄCZONY
                        y_nagrz = 0; // Nagrzewnica wyłączona podczas rozruchu wentylatora (do czasu jej własnego opóźnienia)
                        y_chlod = 0; // Chłodnica wyłączona
                        y_wymiennika = 100; // Pełny odzysk ciepła podczas rozruchu (jak w oryginalnej logice)

                        if (CzasOdStartu < OpoznienieZalaczeniaNagrzewnicy_s - Ts)
                        {
                            CzasOdStartu += Ts;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Praca;
                            // CzasOdStartu nie jest tu resetowany; spełnił swoją rolę dla opóźnienia nagrzewnicy
                        }
                        break;
                    }

                case eStanyPracyCentrali.Praca:
                    {
                        boPracaWentylatoraNawiewu = true;
                        y_nagrz = 0; // Domyślnie wyłączona, zostanie obliczona
                        y_chlod = 0; // Domyślnie wyłączona, zostanie obliczona
                        y_wymiennika = 100; // Domyślnie pełny odzysk w trybie Praca, zostanie dostosowany

                        if (!boStart) // Jeśli otrzymano polecenie stopu
                        {
                            StanPracyCentrali = eStanyPracyCentrali.WychladzanieNagrzewnicy;
                            y_nagrz = 0; // Wyłącz nagrzewnicę
                            y_chlod = 0; // Wyłącz chłodnicę
                            y_wymiennika = 0; // Pełny bypass
                                              // CzasOdStopu zostanie zainicjowany w WychladzanieNagrzewnicy lub jest już resetowany w Stop
                            break;
                        }

                        // Cascade PI Control
                        // RegPI: Calculates desired supply air temperature based on room error
                        double zadana_temp_nawiewu_pr = RegPI.Wyjscie(t_zad - t_pom);

                        // RegPI2: Calculates control action based on supply air temperature error
                        double y_proc_pr = RegPI2.Wyjscie(zadana_temp_nawiewu_pr - t_naw);

                        // --- START: Prioritize Heater Frost Protection based on Exhaust Temperature ---
                        // (This logic might be better named "Heater Low Temperature Protection")
                        double tempWyrzutni = DaneWejsciowe.Czytaj(eZmienne.TempWyrzutni_C);
                        double y_zab_nagrz_pr_check = RegPIzabNagrzewnicy.Wyjscie(ZadanaTempCzynnika - tempWyrzutni);

                        // Check if the heater safety is actively trying to heat significantly
                        if (y_zab_nagrz_pr_check > 50 && (ZadanaTempCzynnika - tempWyrzutni > 1))
                        {
                            y_nagrz = y_zab_nagrz_pr_check; // Heater controlled by safety
                            y_nagrz = Math.Max(0, Math.Min(100, y_nagrz)); // Clamp

                            y_chlod = 0; // Turn off cooling if heater safety is active
                            y_wymiennika = 100; // Force full recovery

                            RegPI2.calka = 0; // Reset integral of the main PI2 controller
                        }
                        else // Normal operation if heater safety is not strongly active
                        {
                            // --- END: Prioritize Heater Low Temperature Protection ---

                            // Cooler Control Logic
                            if (y_proc_pr < zalaczenieChlodnicy)
                            {
                                y_chlod = (y_proc_pr - zalaczenieChlodnicy) * 100 / (RegPI2.min - zalaczenieChlodnicy);
                                y_chlod = Math.Max(0, Math.Min(100, y_chlod)); // Clamp to 0-100
                            }

                            // Heat Exchanger Control Logic
                            if (y_proc_pr < wymiennikChlodu_min) // Demand for cooling contribution from recovery
                            {
                                // If exhaust air is cooler than outdoor air, use recovery for pre-cooling
                                if (DaneWejsciowe.Czytaj(eZmienne.TempWywiewu_C) <= DaneWejsciowe.Czytaj(eZmienne.TempCzerpni_C))
                                {
                                    y_wymiennika = (Math.Max(y_proc_pr, wymiennikChlodu_max) - wymiennikChlodu_min) * 100 / (wymiennikChlodu_max - wymiennikChlodu_min);
                                }
                                else // Otherwise, bypass (outdoor air might be better for free cooling)
                                {
                                    y_wymiennika = 0; // Full bypass
                                }
                                y_wymiennika = Math.Max(0, Math.Min(100, y_wymiennika)); // Clamp
                            }
                            else if (y_proc_pr > wymiennikCiepla_min) // Demand for heating contribution from recovery
                            {
                                y_wymiennika = (Math.Min(y_proc_pr, wymiennikCiepla_max) - wymiennikCiepla_min) * 100 / (wymiennikCiepla_max - wymiennikCiepla_min);
                                y_wymiennika = Math.Max(0, Math.Min(100, y_wymiennika)); // Clamp
                            }
                            // else: y_wymiennika remains at its default (100% recovery) if in deadband

                            // Heater Control Logic
                            if (y_proc_pr > zalaczenieNagrzewnicy)
                            {
                                y_nagrz = (y_proc_pr - zalaczenieNagrzewnicy) * 100 / (RegPI2.max - zalaczenieNagrzewnicy);
                                y_nagrz = Math.Max(0, Math.Min(100, y_nagrz)); // Clamp
                            }

                            // Heater Safety PI Controller (based on exhaust air temperature) - secondary check
                            double y_zab_nagrz_pr_normal = RegPIzabNagrzewnicy.Wyjscie(ZadanaTempCzynnika - DaneWejsciowe.Czytaj(eZmienne.TempWyrzutni_C));
                            y_nagrz = Math.Max(y_zab_nagrz_pr_normal, y_nagrz); // Safety can override or increase heating
                            y_nagrz = Math.Max(0, Math.Min(100, y_nagrz)); // Clamp
                        }


                        // Heat Exchanger Frost Protection PI Controller (based on temp after recovery)
                        double y_zab_wymiennika_pr = RegPIzabWymiennika.Wyjscie(ZadanaTempZaWymiennikiem - DaneWejsciowe.Czytaj(eZmienne.TempZaOdzyskiem_C));
                        y_wymiennika = Math.Min(100 - y_zab_wymiennika_pr, y_wymiennika); // Safety reduces recovery % (opens bypass)
                        y_wymiennika = Math.Max(0, Math.Min(100, y_wymiennika)); // Clamp

                        break;
                    }

                case eStanyPracyCentrali.WychladzanieNagrzewnicy:
                    {
                        boPracaWentylatoraNawiewu = true; // Keep fan running
                        y_nagrz = 0; // Heater off
                        y_chlod = 0; // Cooler off
                        y_wymiennika = 0; // Full bypass

                        if (CzasOdStopu < OpoznienieWylaczeniaWentylatora_s - Ts)
                        {
                            CzasOdStopu += Ts;
                        }
                        else
                        {
                            StanPracyCentrali = eStanyPracyCentrali.Stop; // Transition to Stop
                            boPracaWentylatoraNawiewu = false; // Stop fan
                            CzasOdStopu = 0; // Reset timer
                        }
                        break;
                    }
            }

            // Emergency Frost Thermostat for Water Heater Coil (Critical Safety)
            if (0 < DaneWejsciowe.Czytaj(eZmienne.TermostatPZamrNagrzewnicyWodnej))
            {
                y_nagrz = 100; // Force heater to 100%
                y_chlod = 0;   // Cooler off
                boPracaWentylatoraNawiewu = false; // Stop fans immediately
                StanPracyCentrali = eStanyPracyCentrali.Stop; // Go to Stop state
                // Consider adding an alarm flag here
            }

            // Pump control based on heater/cooler operation
            if (y_chlod > 0)
            {
                DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyChlodnicyWodnej, 1); // Enable cooler pump
            }
            else
            {
                DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyChlodnicyWodnej, 0); // Disable cooler pump
            }

            if (y_nagrz > 0)
            {
                DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej1, 1); // Enable heater pump
            }
            else
            {
                DaneWyjsciowe.Zapisz(eZmienne.ZalaczeniePompyNagrzewnicyWodnej1, 0); // Disable heater pump
            }

            // Write output values
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieNagrzewnicy1_pr, y_nagrz);     // Heater modulation %
            DaneWyjsciowe.Zapisz(eZmienne.WysterowanieChlodnicy_pr, y_chlod);       // Cooler modulation %
            DaneWyjsciowe.Zapisz(eZmienne.Wysterowanie_bypass_pr, 100 - y_wymiennika); // Bypass: 0% = full recovery, 100% = bypass open
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraNawiewu, boPracaWentylatoraNawiewu); // Supply fan enable
            DaneWyjsciowe.Zapisz(eZmienne.ZezwolenieNaPraceWentylatoraWywiewu, boPracaWentylatoraNawiewu); // Exhaust fan enable (assuming linked to supply)

            return 0; // Success
        }

        // Method to change parameters via fmParametry form
        public void ZmienParametry()
        {
            // UWAGA: Poniższy kod zakłada istnienie formularza fmParametry.
            // Należy dodać do fmParametry pole do edycji OpoznienieStartuWentylatora_s,
            // np. public double t_start_wentylatora_delay; oraz odpowiednią kontrolkę.

            fmParametry fm = new fmParametry(); // Create instance of parameters form

            // Pass current regulator parameters to the form
            fm.kp = RegPI.kp;
            fm.ki = RegPI.ki;
            fm.maxTempNawiewu = RegPI.max;
            fm.minTempNawiewu = RegPI.min;

            fm.kp2 = RegPI2.kp;
            fm.ki2 = RegPI2.ki;
            fm.nag100 = RegPI2.max;
            fm.ch100 = RegPI2.min;

            fm.ch0 = zalaczenieChlodnicy;
            fm.nag0 = zalaczenieNagrzewnicy;
            fm.wci0 = wymiennikCiepla_min;
            fm.wci100 = wymiennikCiepla_max;
            fm.wch0 = wymiennikChlodu_min;
            fm.wch100 = wymiennikChlodu_max;

            fm.t1 = OpoznienieZalaczeniaNagrzewnicy_s;
            fm.t2 = OpoznienieWylaczeniaWentylatora_s;
            // PRZYPISANIE DO FORMULARZA - zastąp 'fm.t_start_wentylatora_delay' rzeczywistą nazwą pola w fmParametry
            // fm.t_start_wentylatora_delay = OpoznienieStartuWentylatora_s; 

            fm.zNagP = RegPIzabNagrzewnicy.kp;
            fm.zNagI = RegPIzabNagrzewnicy.ki;
            fm.zWymP = RegPIzabWymiennika.kp;
            fm.zWymI = RegPIzabWymiennika.ki;

            // Note: fmParametry should also have fields for ZadanaTempCzynnika and ZadanaTempZaWymiennikiem
            // if they are to be user-adjustable. Currently, they are not loaded into fm.

            fm.UstawKontrolki(); // Populate form controls with these values

            if (fm.ShowDialog() == System.Windows.Forms.DialogResult.OK) // If user clicks OK
            {
                // Update regulator parameters from the form
                RegPI.kp = fm.kp;
                RegPI.ki = fm.ki;
                RegPI.max = fm.maxTempNawiewu;
                RegPI.min = fm.minTempNawiewu;

                RegPI2.kp = fm.kp2;
                RegPI2.ki = fm.ki2;
                RegPI2.max = fm.nag100;
                RegPI2.min = fm.ch100;

                RegPIzabNagrzewnicy.kp = fm.zNagP;
                RegPIzabNagrzewnicy.ki = fm.zNagI;
                RegPIzabWymiennika.kp = fm.zWymP;
                RegPIzabWymiennika.ki = fm.zWymI;

                zalaczenieChlodnicy = fm.ch0;
                zalaczenieNagrzewnicy = fm.nag0;
                wymiennikCiepla_min = fm.wci0;
                wymiennikCiepla_max = fm.wci100;
                wymiennikChlodu_min = fm.wch0;
                wymiennikChlodu_max = fm.wch100;

                OpoznienieZalaczeniaNagrzewnicy_s = fm.t1;
                OpoznienieWylaczeniaWentylatora_s = fm.t2;
                // ODCZYT Z FORMULARZA - zastąp 'fm.t_start_wentylatora_delay' rzeczywistą nazwą pola w fmParametry
                // OpoznienieStartuWentylatora_s = fm.t_start_wentylatora_delay; 

                // Update ZadanaTempCzynnika and ZadanaTempZaWymiennikiem if they were added to fmParametry
                // e.g., ZadanaTempCzynnika = fm.zadanaTempCzynnika;  
            }
        }
    }
}