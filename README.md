# Asetto Corsa Multi-Class Race Dashboard

<img width="1887" height="857" alt="image" src="https://github.com/user-attachments/assets/d010cb12-9687-416a-9a32-1d494e9a91ef" />
<img width="1875" height="836" alt="image" src="https://github.com/user-attachments/assets/4b9ee6f1-8b83-4b82-8c4d-54cdb76342fd" />
<img width="1878" height="903" alt="image" src="https://github.com/user-attachments/assets/d97860be-816a-4f5a-a3db-ebcf5589afd0" />
<img width="1878" height="878" alt="image" src="https://github.com/user-attachments/assets/03a4b6a9-4e4f-48d7-9469-271cc1b78831" />

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
- The dashboard installed (see above)

## First-time setup

- **Game Path** — You have to choose the correct game path folder (Assetto Corsa Game Rooth Path) during the installation wizard.

## Running a race — step by step

1. **Create your championship.** From the **Championship** page, add a championship, then add a season to it.
2. **Create the races.** Open the season and add each race event (name, country, and a point system — create one on the **Point Settings** page first if you haven't).
3. **Add teams and drivers.** Use the **Teams** and **Drivers** pages. You can create drivers one by one, or add a whole batch from a preset file.
4. **Set up the event.** Open the race and use the **Teams** tab to choose which teams are taking part.
5. **Practice and Qualifying.** After the session, import its result file. The race status moves to **in progress**. You can then export the qualifying grid and the race preset, ready to open in Content Manager.
6. **The race.** After the race finishes, import its result file from the race results folder. The dashboard builds the standings, applies the points, and marks the race as **finished**.

## Lua App - Multiclass Leaderboard (Driver posittion)

1. You have to configure the car tags in Content Manager to make the leaderboard work correctly.
<img width="816" height="602" alt="image" src="https://github.com/user-attachments/assets/ee3cd70e-8090-400b-9576-504072184f6a" />

2. In game class-leaderboard config - You have to type the car tag to make the leaderboard auto order by class: eg: Hyperpar
<img width="490" height="561" alt="image" src="https://github.com/user-attachments/assets/7188cb24-4dbc-4d42-a35b-d2755f13dde6" />

## Rolling Start

**Rolling start is a MUST** You can not turn it off, during the formation lap, human driver can accelerate freely, no speed limit. You must respect the AI drivers in the single or double file formation.

## Tips

- Round status updates as you go: no data means "not started", practice/qualifying loaded means "in progress", race loaded means "finished".
- If two drivers use the same car and skin combo, load their results once with the right driver selected so they are not mixed up.
- If a result needs correcting, import the file again in place of the old one.
- Results come from the Multi_Class output; make sure the folder you enter in Game Config is the one those files are saved to.

## License

This project is licensed under the **MIT License** with additional terms. See the [`LICENSE`](LICENSE) file for the full text.

- You may **read, study, and contribute** to the source code.
- The **author credit** ("Duong Do") must not be removed or changed in any copy or derivative work.
- Redistributing or publishing the **complete source code** as a standalone repository or release is **not permitted**.
- Results come from the [Multi Class Race Leaderboard](https://github.com/duongdt011099/ac-multi-class-race) output; make sure the folder you enter in Game Config is the one those files are saved to.
