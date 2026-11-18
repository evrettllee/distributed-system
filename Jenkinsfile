pipeline {
    agent any

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                bat 'git log -1 --format="Commit: %%H %%s"'
            }
        }

        stage('Tools') {
            steps {
                bat 'dotnet --version'
                bat 'git --version'
                bat 'docker --version'
                bat 'kubectl version --client'
                bat 'kubectl get nodes'
            }
        }
    }
}