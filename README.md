# MAS Arcade
This project is a **Multi Agent System (MAS)** for an arcade game room. The system enables the user through vr in unity to interact with the agents in the arcade, which are implemented using JaCaMo. JaCaMo and Unity are connected through the Vesna-Unity library and the communication between agents and the user is implemented using ChatBDI.

## Installation
To run the project, you need to have installed on your machine:
- Unity (version 6000.0.47f1)
- Java 21
- Gradle
- git-lfs

## Usage
To run the project, follow these steps:
1. Clone the repository to your local machine.
2. Open the env folder in Unity.
3. Open the Arcade scene.
4. Run the scene in Unity.
5. Open the mind folder in the terminal and run the command `gradle run` to start the JaCaMo agents.
6. Interact with the agents in the arcade through vr in Unity.

## Technologies Used
- Unity: A cross-platform game engine used for developing the arcade environment and vr interactions.
- JaCaMo: A framework for developing multi-agent systems, used to implement the agents in the arcade.
- Vesna-Unity: A library that enables communication between JaCaMo agents and Unity.
- ChatBDI: A communication protocol used for interaction between agents and the user.

## Background
The MAS Arcade project was developed with the goal of extending the pre-existing library Vesna-Unity. The initial version of this project can be found at [this repository](https://github.com/VEsNA-ToolKit/vesna-unity) under the branch `ZirpoliM`.


## Goals
Our work focused on:
- Enhancing the interaction between the user and the Cartago Artifacts, allowing the interaction to follow the same logic as the interaction with the agents.
- Enabling the user to interact with the agents through vr in Unity, allowing for a more immersive experience.

## Project Structure
The project is organized into the following folders:
- `env`: Contains the Unity project files, including the Arcade scene and vr interactions.
    - `Assets`: Contains the Unity assets, including models, textures, and scripts.
        - `Scenes`: Contains the Arcade scene.
        - `script`: Contains the scripts for agents, artfacts, and vr interactions.
        - `scripts`: Contains the scripts of the raycast.

- `mind`: Contains the JaCaMo project files, including the agent definitions and behaviors.
    - `src`: Contains the source code for the JaCaMo agents and artifacts.
        - `agt`: Contains the agents definition, ChatBDI implementation and vesna.
        - `env/artifact`: Contains the Cartago artifacts definition.
