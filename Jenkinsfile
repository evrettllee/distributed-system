pipeline {
    agent any

    options {
        skipDefaultCheckout(true)
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                bat 'git log -1 --format="Commit: %%H %%s"'
            }
        }

        stage('Restore') {
            steps {
                bat 'dotnet restore DistributedSystem.slnx'
            }
        }

        stage('Build') {
            steps {
                bat 'dotnet build DistributedSystem.slnx --configuration Release --no-restore'
            }
        }
    }
}