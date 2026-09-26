# Asetto Corsa Multi-Class Race Dashboard
<img width="1346" height="676" alt="image" src="https://github.com/user-attachments/assets/a791cc09-e3bb-48e2-bf33-be8da80dbabd" />
<img width="1331" height="590" alt="image" src="https://github.com/user-attachments/assets/f0f622b7-40a6-446e-a582-975373cd0645" />

<span style="color:red"><strong><em>Prerequisites: This application requires Multi Class Race Leaderboard app to work properly in Practice/Qualifying Sessions</em></strong></span>

The Multi-Class Race Dashboard helps you run and manage a multi-class racing championship — Hypercar, LMP2 and GT3 — in Assetto Corsa and Content Manager. It covers everything from your entry list to the final race results and championship points, all in one place.

## What you can do

- **Manage your series** — create championships, add seasons, create races, and watch each race move through its status: not started, in progress (after practice/qualifying results are loaded), and finished (after the race is done).
- **Manage teams and drivers** — set each driver's car and skin, AI strength and aggression, nationality, and flag who is driving for real (human driver).
- **Set up and run race sessions** — choose which teams race in which event, then run Practice, Qualifying and Race sessions.
- **Import results automatically** — load practice/qualifying results and the final race result, and the dashboard handles standings and points for you.
- **Create ready-to-load grids** — generate grid and race preset files you can open directly in Content Manager, ordered by class and qualifying position.
- **See the cars** — preview each car's livery right from your game installation.
- **Score the championship your way** — set up your own point systems (who earns points for finishing where) and apply them per race.

## Installing the app

1. Open the **GitHub Releases** page for this project.
2. Download the installer (`EnduranceRaceDashboardSetup.exe`).
3. Run the installer. If Windows shows a security prompt, choose **More info → Run anyway**.
4. Follow the on-screen steps and choose where to install it.
5. When finished, open your browser and go to `http://127.0.0.1:5000` — bookmark it for easy access.
6. To update later, download the newest installer and run it again; your data is kept.

## Before you start

To use the dashboard you need:

- Assetto Corsa installed
- Content Manager installed
- The Multi_Class Lua app installed for Content Manager (this is what produces the result files the dashboard reads)
- The dashboard installed (see above)

## First-time setup

Open **Game Config** from the menu. You will set up three things:

- **Game Path** — the folder where Assetto Corsa is installed. Needed so the dashboard can show car liveries.
- **Preset Path** — the folder Content Manager keeps its grid presets in. Needed when exporting grid and race presets.
- **Race Results Path** — the folder where your race result files land. Needed when importing race results.

Save the configuration once and you are ready to go.

## Running a race — step by step

1. **Create your championship.** From the **Championship** page, add a championship, then add a season to it.
2. **Create the races.** Open the season and add each race event (name, country, and a point system — create one on the **Point Settings** page first if you haven't).
3. **Add teams and drivers.** Use the **Teams** and **Drivers** pages. You can create drivers one by one, or add a whole batch from a preset file.
4. **Set up the event.** Open the race and use the **Teams** tab to choose which teams are taking part.
5. **Practice and Qualifying.** After the session, import its result file (the one produced by the Multi_Class app). The race status moves to **in progress**. You can then export the qualifying grid and the race preset, ready to open in Content Manager.
6. **The race.** After the race finishes, import its result file from the race results folder. The dashboard builds the standings, applies the points, and marks the race as **finished**.

## Tips

- Round status updates as you go: no data means "not started", practice/qualifying loaded means "in progress", race loaded means "finished".
- If two drivers use the same car and skin combo, load their results once with the right driver selected so they are not mixed up.
- If a result needs correcting, import the file again in place of the old one.
- Results come from the Multi_Class output; make sure the folder you enter in Game Config is the one those files are saved to.
